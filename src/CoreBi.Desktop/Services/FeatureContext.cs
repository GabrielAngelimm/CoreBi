using System.Windows;
using CoreBi.Engine.Games;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Persistence;

namespace CoreBi.Desktop.Services;

/// <summary>
/// Contexto único compartilhado entre o shell (MainWindow) e todas as 14 páginas: coordenação de workloads,
/// telemetria compartilhada, configurações, ciclo de vida e ações de navegação/notificação.
/// </summary>
internal sealed class FeatureContext
{
    private readonly object _criticalSync = new();
    private readonly List<string> _criticalMessages = new();

    public FeatureContext(
        Window owner,
        WorkloadCoordinator workloads,
        CancellationToken lifetime,
        AppSettings settings,
        SensorMonitor sensors,
        FrameMonitor frames,
        GpuMonitor gpuTelemetry,
        Action<string> navigate,
        Action<string, string, string, string, string> recordEvent,
        Action<string, string, string> notify,
        Func<bool> saveSettings,
        Action syncFrameMonitoring,
        Action<bool, bool> setBenchmarkIsolation,
        Func<bool> isLiveMotion)
    {
        Owner = owner;
        Workloads = workloads;
        Lifetime = lifetime;
        Settings = settings;
        Sensors = sensors;
        Frames = frames;
        GpuTelemetry = gpuTelemetry;
        Navigate = navigate;
        RecordEvent = recordEvent;
        Notify = notify;
        SaveSettings = saveSettings;
        SyncFrameMonitoring = syncFrameMonitoring;
        SetBenchmarkIsolation = setBenchmarkIsolation;
        IsLiveMotion = isLiveMotion;
    }

    public Window Owner { get; }
    public WorkloadCoordinator Workloads { get; }
    public CancellationToken Lifetime { get; }
    public AppSettings Settings { get; }
    public SensorMonitor Sensors { get; }
    public FrameMonitor Frames { get; }
    public GpuMonitor GpuTelemetry { get; }

    public Action<string> Navigate { get; }
    public Action<string, string, string, string, string> RecordEvent { get; }
    public Action<string, string, string> Notify { get; }
    public Func<bool> SaveSettings { get; }
    public Action SyncFrameMonitoring { get; }
    public Action<bool, bool> SetBenchmarkIsolation { get; }
    public Func<bool> IsLiveMotion { get; }

    public IReadOnlyList<GpuInfo> Gpus { get; internal set; } = Array.Empty<GpuInfo>();
    public IReadOnlyList<DriveCard> Drives { get; internal set; } = Array.Empty<DriveCard>();
    public string CpuName { get; internal set; } = "";
    public GpuInfo? PrimaryGpu => Gpus.Count > 0 ? Gpus[0] : null;

    public bool IsClosing => Lifetime.IsCancellationRequested;

    public bool HasCriticalOperation
    {
        get { lock (_criticalSync) return _criticalMessages.Count > 0; }
    }

    public string CriticalOperationMessage
    {
        get
        {
            lock (_criticalSync)
                return _criticalMessages.Count > 0 ? _criticalMessages[^1] : "Aguarde a operação em andamento terminar antes de fechar o CoreBi.";
        }
    }

    public IDisposable BeginCriticalOperation(string? blockingMessage = null)
    {
        string message = string.IsNullOrWhiteSpace(blockingMessage)
            ? "Aguarde a operação em andamento terminar antes de fechar o CoreBi."
            : blockingMessage;
        lock (_criticalSync) _criticalMessages.Add(message);
        return new CriticalScope(this, message);
    }

    public GpuLiveSample? ReadPrimaryGpuLive() =>
        PrimaryGpu is { } gpu ? GpuMonitor.ReadLive(gpu, Sensors.Snapshot()) : null;

    public CpuLiveSample ReadCpuLive() => GpuMonitor.ReadCpu(Sensors, Sensors.Snapshot());

    // Temperatura e consumo sozinhos não pagam a leitura completa (seis consultas NVML): são pedidos a cada segundo
    // por várias páginas, inclusive durante benchmarks, quando o driver deve ser poupado.
    public float? PrimaryGpuTemp() => PrimaryGpu is { } gpu ? GpuMonitor.ReadTemp(gpu, Sensors.Snapshot()) : null;
    public float? PrimaryGpuPower() => PrimaryGpu is { } gpu ? GpuMonitor.ReadPower(gpu, Sensors.Snapshot()) : null;
    public float? PrimaryGpuLoad() => PrimaryGpu?.WindowsLoad;

    public string PrimaryGpuPerfCap() =>
        PrimaryGpu?.LiveExtras().FirstOrDefault(kv => kv.Key.StartsWith("PerfCap", StringComparison.Ordinal)).Value ?? "";

    private sealed class CriticalScope(FeatureContext context, string message) : IDisposable
    {
        private FeatureContext? _context = context;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _context, null);
            if (current == null) return;
            lock (current._criticalSync) current._criticalMessages.Remove(message);
        }
    }
}
