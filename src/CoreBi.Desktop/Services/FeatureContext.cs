using System.Windows;
using CoreBi.Engine.Games;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Persistence;

namespace CoreBi.Desktop.Services;

/// <summary>Registra um evento na atividade recente da Visão geral.</summary>
internal delegate void RecordEventHandler(string icon, string title, string detail, string value, string channel);

/// <summary>Aviso no canto da tela (também vai para a atividade recente).</summary>
internal delegate void NotifyHandler(string title, string detail, string icon);

/// <summary>Durante um benchmark: pausa as leituras de sensores (driver) e, se pedido, suspende o overlay.</summary>
internal delegate void BenchmarkIsolationHandler(bool pauseSensors, bool suspendOverlay);

/// <summary>
/// Contexto único compartilhado entre o shell (MainWindow) e todas as 14 páginas: coordenação de workloads,
/// telemetria compartilhada, configurações, ciclo de vida e ações de navegação/notificação.
/// O shell monta uma instância e cada página a recebe em <c>Initialize</c>; não é um contêiner de serviços.
/// </summary>
internal sealed class FeatureContext
{
    private const string DefaultCriticalMessage = "Aguarde a operação em andamento terminar antes de fechar o CoreBi.";

    private readonly object _criticalSync = new();
    private readonly List<string> _criticalMessages = new();

    public required Window Owner { get; init; }
    public required WorkloadCoordinator Workloads { get; init; }
    public required CancellationToken Lifetime { get; init; }
    public required AppSettings Settings { get; init; }
    public required SensorMonitor Sensors { get; init; }
    public required FrameMonitor Frames { get; init; }

    public required Action<string> Navigate { get; init; }
    public required RecordEventHandler RecordEvent { get; init; }
    public required NotifyHandler Notify { get; init; }
    public required Func<bool> SaveSettings { get; init; }
    public required Action SyncFrameMonitoring { get; init; }
    public required BenchmarkIsolationHandler SetBenchmarkIsolation { get; init; }
    public required Func<bool> IsLiveMotion { get; init; }

    public IReadOnlyList<GpuInfo> Gpus { get; internal set; } = Array.Empty<GpuInfo>();
    public IReadOnlyList<DriveCard> Drives { get; internal set; } = Array.Empty<DriveCard>();
    public string CpuName { get; internal set; } = "";
    public GpuInfo? PrimaryGpu => Gpus.Count > 0 ? Gpus[0] : null;
    /// <summary>Nome do processador e placas de vídeo já lidos na abertura (com sucesso ou não).</summary>
    public bool HardwareRead { get; internal set; }
    /// <summary>A primeira leitura dos sensores já terminou na abertura (com sucesso ou não).</summary>
    public bool SensorsRead { get; internal set; }

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
                return _criticalMessages.Count > 0 ? _criticalMessages[^1] : DefaultCriticalMessage;
        }
    }

    public IDisposable BeginCriticalOperation(string? blockingMessage = null)
    {
        string message = string.IsNullOrWhiteSpace(blockingMessage) ? DefaultCriticalMessage : blockingMessage;
        lock (_criticalSync) _criticalMessages.Add(message);
        return new CriticalScope(this, message);
    }

    public CpuLiveSample ReadCpuLive() => CpuTelemetry.Read(Sensors, Sensors.Snapshot());

    // Temperatura, consumo e limitador sozinhos não pagam a leitura completa (GpuMonitor.ReadLive, seis consultas NVML):
    // são pedidos a cada segundo por várias páginas, e cada um custa no máximo uma consulta ao driver.
    public float? PrimaryGpuTemp() => PrimaryGpu is { } gpu ? GpuMonitor.ReadTemp(gpu, Sensors.Snapshot()) : null;
    public float? PrimaryGpuPower() => PrimaryGpu is { } gpu ? GpuMonitor.ReadPower(gpu, Sensors.Snapshot()) : null;
    public float? PrimaryGpuLoad() => PrimaryGpu?.WindowsLoad;

    public string PrimaryGpuPerfCap() => PrimaryGpu?.PerfCapNow() ?? "";

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
