using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Games;

public sealed record FrameStats(int ProcessId, string ProcessName, double Fps, double FrameTimeMs, double Low1Fps,
                                IReadOnlyList<double> RecentFrameTimes);

/// <summary>
/// Mede o FPS de qualquer processo sem injetar código, por eventos de apresentação: escuta via ETW os eventos
/// "Present" que o Windows emite a cada frame entregue por DXGI (DirectX 10/11/12) e Direct3D 9.
/// Jogos Vulkan/OpenGL só aparecem se apresentarem via DXGI (comum em drivers recentes, não garantido).
/// </summary>
public sealed class FrameMonitor : IDisposable
{
    private readonly string _sessionName = $"CoreBi-Frames-{Environment.ProcessId}-{Guid.NewGuid():N}";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42, D3D9PresentStart = 1;
    private const double HistoryMs = 10_000; // janela para o 1% low

    private TraceEventSession? _session;
    private readonly object _sessionLock = new();
    private bool _disposed;
    private Thread? _thread;
    private readonly object _lock = new();
    private sealed class PresentStream
    {
        public Queue<double> Times { get; } = new();
        public double Last { get; set; } = double.NegativeInfinity;
    }
    private readonly Dictionary<int, Dictionary<ulong, PresentStream>> _presents = new();
    private readonly Dictionary<int, long> _lastSeen = new();           // pid → Stopwatch do último evento recebido
    private long _lastCleanup;

    public bool Running => _thread?.IsAlive == true;
    public string? Error { get; private set; }

    public void Start()
    {
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Running) return;
            try
            {
                var session = new TraceEventSession(_sessionName, TraceEventSessionOptions.Create) { StopOnDispose = true };
                _session = session;
                _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue, null);
                _session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, ulong.MaxValue, null);
                _session.Source.AllEvents += OnEvent;
                _thread = new Thread(() =>
                {
                    try { session.Source.Process(); }
                    catch (Exception ex) { if (ReferenceEquals(_session, session)) Error = ex.Message; }
                })
                { IsBackground = true, Name = "ETW frames" };
                Error = null;
                _thread.Start();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _session?.Dispose();
                _session = null;
            }
        }
    }

    private void OnEvent(TraceEvent e)
    {
        if (_session == null) return;
        int id = (int)e.ID;
        bool present = (e.ProviderGuid == DxgiProvider && id == DxgiPresentStart) ||
                       (e.ProviderGuid == D3D9Provider && id == D3D9PresentStart);
        if (!present) return;

        ulong stream = 0;
        foreach (string name in e.PayloadNames)
        {
            if (!name.Contains("SwapChain", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                object value = e.PayloadByName(name);
                stream = value is IntPtr pointer ? unchecked((ulong)pointer.ToInt64()) : Convert.ToUInt64(value);
            }
            catch { stream = 0; }
            break;
        }
        RecordPresent(e.ProcessID, stream, e.TimeStampRelativeMSec);
    }

    internal void RecordPresent(int pid, ulong stream, double t)
    {
        lock (_lock)
        {
            if (!_presents.TryGetValue(pid, out var streams)) _presents[pid] = streams = new();
            if (!streams.TryGetValue(stream, out var history)) streams[stream] = history = new();
            if (t <= history.Last) return;
            var q = history.Times;
            q.Enqueue(t);
            history.Last = t;
            while (q.Count > 0 && q.Peek() < t - HistoryMs) q.Dequeue();
            foreach (var old in streams.Where(x => x.Value.Last < t - HistoryMs).Select(x => x.Key).ToArray()) streams.Remove(old);
            _lastSeen[pid] = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(_lastCleanup).TotalSeconds >= 10)
            {
                foreach (int old in _lastSeen.Where(x => Stopwatch.GetElapsedTime(x.Value).TotalSeconds > 10).Select(x => x.Key).ToArray())
                {
                    _lastSeen.Remove(old);
                    _presents.Remove(old);
                }
                _lastCleanup = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <summary>Estatísticas do processo, ou null se ele não apresentou frames nos últimos 2 s.</summary>
    public FrameStats? GetStats(int pid)
    {
        double[] ts;
        lock (_lock)
        {
            if (!_presents.TryGetValue(pid, out var streams) || streams.Count == 0) return null;
            double newest = streams.Values.Max(s => s.Last);
            var q = streams.Values.OrderByDescending(s => s.Times.Count(t => t >= newest - 1000)).First().Times;
            if (q.Count < 3) return null;
            if (Stopwatch.GetElapsedTime(_lastSeen[pid]).TotalSeconds > 2) return null;
            ts = q.ToArray();
        }

        double last = ts[^1];
        var frameTimes = new List<double>(ts.Length);
        for (int i = 1; i < ts.Length; i++) frameTimes.Add(ts[i] - ts[i - 1]);

        // FPS do último segundo (pelo relógio dos eventos, imune ao atraso de entrega do ETW).
        int first = Array.FindIndex(ts, x => x >= last - 1000);
        double span = last - ts[first];
        int frames = ts.Length - 1 - first;
        double fps = span > 0 ? frames * 1000 / span : 0;

        // 1% low: FPS equivalente ao percentil 99 do frametime nos últimos 10 s.
        var sorted = frameTimes.OrderBy(x => x).ToArray();
        double p99 = sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(sorted.Length * 0.99))];
        double low1 = p99 > 0 ? 1000 / p99 : 0;

        var recent = frameTimes.Skip(Math.Max(0, frameTimes.Count - 120)).ToList();
        return new FrameStats(pid, ProcessName(pid), fps, fps > 0 ? 1000 / fps : 0, low1, recent);
    }

    /// <summary>Estatísticas do programa em primeiro plano (o jogo que está na tela).</summary>
    public FrameStats? GetForegroundStats()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 || pid == Environment.ProcessId ? null : GetStats((int)pid);
    }

    private static string ProcessName(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.ProcessName; }
        catch { return $"PID {pid}"; }
    }

    public void Stop()
    {
        lock (_sessionLock)
        {
            var session = _session;
            _session = null;
            var thread = _thread;
            try { session?.Source.StopProcessing(); } catch (Exception) { }
            try { session?.Dispose(); } catch (Exception ex) { DiagnosticLog.Write("frames.stop", ex); }
            if (thread != null && thread != Thread.CurrentThread && !thread.Join(3000))
                Error = "O coletor ETW ainda está encerrando.";
            if (thread?.IsAlive != true) _thread = null;
            lock (_lock) { _presents.Clear(); _lastSeen.Clear(); }
        }
    }

    public void Dispose()
    {
        lock (_sessionLock) { if (_disposed) return; _disposed = true; }
        Stop();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
