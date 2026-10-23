using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Graphics;

/// <summary>
/// Uso WDDM: soma os processos de cada motor e escolhe o motor mais ocupado de cada adaptador,
/// como o Gerenciador de Tarefas. PDH roda fora do dispatcher, uma vez por segundo.
/// </summary>
internal sealed class WindowsGpuUsage : IDisposable
{
    private static readonly Regex Instance = new(
        @"^pid_\d+_luid_0x([0-9a-f]+)_0x([0-9a-f]+)_phys_(\d+)_eng_(\d+)_engtype_",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private const uint MoreData = 0x800007D2, DoubleFormat = 0x200;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private IReadOnlyDictionary<ulong, float> _loads = new Dictionary<ulong, float>();
    private int _disposed;
    internal string Diagnostic { get; private set; } = "Starting";

    public WindowsGpuUsage() => _worker = Task.Run(CollectAsync);

    public float? GetLoad(ulong? adapterLuid)
    {
        var loads = Volatile.Read(ref _loads);
        return adapterLuid is ulong luid && loads.TryGetValue(luid, out float load) ? load : null;
    }

    private async Task CollectAsync()
    {
        try
        {
            _stop.Token.ThrowIfCancellationRequested();
            uint status = PdhOpenQuery(null, 0, out var query);
            using (query)
            {
                if (status != 0) { Diagnostic = $"OpenQuery: 0x{status:X8}"; return; }
                _stop.Token.ThrowIfCancellationRequested();
                // English counter names also work on a Portuguese Windows installation.
                status = PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", 0, out var counter);
                if (status != 0) { Diagnostic = $"AddCounter: 0x{status:X8}"; return; }
                Diagnostic = "Priming";
                PdhCollectQueryData(query); // Rate counters need a baseline and a second sample.
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    status = PdhCollectQueryData(query);
                    var loads = status == 0 ? ReadLoads(counter) : new Dictionary<ulong, float>();
                    if (status != 0) Diagnostic = $"CollectQuery: 0x{status:X8}";
                    Volatile.Write(ref _loads, loads);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Diagnostic = ex.ToString(); DiagnosticLog.Write("gpu.windows-usage", ex); }
        finally { Volatile.Write(ref _loads, new Dictionary<ulong, float>()); }
    }

    private IReadOnlyDictionary<ulong, float> ReadLoads(IntPtr counter)
    {
        uint size = 0;
        uint status = PdhGetFormattedCounterArray(counter, DoubleFormat, ref size, out _, IntPtr.Zero);
        if (status != MoreData || size == 0 || size > 16_777_216)
        { Diagnostic = $"ArraySize: 0x{status:X8}, {size} bytes"; return new Dictionary<ulong, float>(); }
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PdhGetFormattedCounterArray(counter, DoubleFormat, ref size, out uint count, buffer);
            if (status != 0) { Diagnostic = $"ArrayValues: 0x{status:X8}"; return new Dictionary<ulong, float>(); }
            int stride = Marshal.SizeOf<CounterItem>();
            if (count > size / stride) return new Dictionary<ulong, float>();
            var values = new List<(string Instance, double Value)>((int)count);
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * stride);
                if (item.Value.Status <= 1 && Marshal.PtrToStringUni(item.Name) is string name)
                    values.Add((name, item.Value.Value));
            }
            var loads = Aggregate(values);
            Diagnostic = $"Items: {count}, valid: {values.Count}, adapters: {loads.Count}";
            return loads;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static IReadOnlyDictionary<ulong, float> Aggregate(IEnumerable<(string Instance, double Value)> values)
    {
        var engines = new Dictionary<(ulong Luid, uint Physical, uint Engine), double>();
        foreach (var (name, value) in values)
        {
            if (!double.IsFinite(value) || value < 0) continue;
            var match = Instance.Match(name);
            if (!match.Success
                || !uint.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint high)
                || !uint.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint low)
                || !uint.TryParse(match.Groups[3].Value, out uint physical)
                || !uint.TryParse(match.Groups[4].Value, out uint engine)) continue;
            var key = (((ulong)high << 32) | low, physical, engine);
            engines[key] = engines.GetValueOrDefault(key) + value;
        }
        return engines.GroupBy(e => e.Key.Luid).ToDictionary(g => g.Key, g => (float)Math.Clamp(g.Max(e => e.Value), 0, 100));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { public IntPtr Name; public CounterValue Value; }

    private sealed class QueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public QueryHandle() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")]
    private static extern uint PdhOpenQuery(string? source, nuint userData, out QueryHandle query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern uint PdhAddEnglishCounter(QueryHandle query, string path, nuint userData, out IntPtr counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(QueryHandle query);
    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr items);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
