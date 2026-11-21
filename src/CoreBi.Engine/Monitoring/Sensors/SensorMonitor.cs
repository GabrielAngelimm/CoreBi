using System.Collections.ObjectModel;
using CoreBi.Engine.Infrastructure;
using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Sensors;

/// <summary>Encapsula o LibreHardwareMonitorLib e mantém a lista de sensores atualizada.</summary>
public sealed class SensorMonitor : IDisposable
{
    private readonly IHardwareSource _source;
    private readonly object _lifecycle = new();
    private readonly HashSet<string> _failedHardware = new();
    private Task? _openTask;
    private bool _disposed;
    private readonly Dictionary<string, SensorRow> _index = new();
    private volatile bool _open;
    private volatile bool _paused, _gpuRetired;
    private HashSet<string>? _networks; // placas de rede físicas e ativas; interfaces virtuais não são lidas
    private static bool? _pawnIoInstalled;

    public ObservableCollection<SensorRow> Rows { get; } = new();
    public bool IsOpen => _open;

    /// <summary>
    /// Pausa todas as leituras (LHM e NVML). Chamado quando o Windows altera dispositivos:
    /// pode ser o driver de vídeo sendo trocado, e ler a placa durante a troca derruba o processo.
    /// </summary>
    public void Pause()
    {
        _paused = true;
        GpuAccessGate.SetSuspended(true);
    }

    public void Resume()
    {
        _paused = false;
        if (!_gpuRetired) GpuAccessGate.SetSuspended(false);
    }

    public bool IsPaused => _paused;

    /// <summary>
    /// O driver de vídeo mudou com o CoreBi aberto: a placa sai das leituras até o app abrir de novo.
    /// A biblioteca de sensores e a NVML ficam presas à DLL do driver anterior, então reconectar não é seguro.
    /// </summary>
    public void RetireGpu()
    {
        lock (_lifecycle)
        {
            _gpuRetired = true;
            GpuAccessGate.SetSuspended(true);
            foreach (var row in Rows.Where(r => SensorTypeMeta.IsGpu(r.HardwareType))) row.Update(null, null, null);
        }
    }

    public bool GpuRetired => _gpuRetired;

    public IReadOnlyList<SensorReading> Snapshot()
    {
        lock (_lifecycle)
            return Rows.Select(r => new SensorReading(r.Id, r.Hardware, r.Name, r.Type, r.HardwareType, r.RawValue, r.Category, r.Value, r.Min, r.Max)).ToArray();
    }

    /// <summary>
    /// Relatório de saúde do próprio SSD NVMe (aviso crítico, erros de mídia, desligamentos inesperados), por nome do disco.
    /// O Windows nem sempre repassa esses contadores; o LibreHardwareMonitor os lê diretamente do disco.
    /// </summary>
    public IReadOnlyDictionary<string, NvmeLog> NvmeLogs()
    {
        var logs = new Dictionary<string, NvmeLog>(StringComparer.Ordinal);
        lock (_lifecycle)
        {
            if (!_open || _disposed || _paused) return logs;
            foreach (var hw in _source.Hardware)
            {
                if (hw.HardwareType != HardwareType.Storage || hw is not LibreHardwareMonitor.Hardware.Storage.StorageDevice device) continue;
                try
                {
                    var smart = device.Storage?.Smart?.SmartAttributes;
                    if (smart == null) continue;
                    ulong? Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType type) =>
                        smart.FirstOrDefault(a => a.Info?.Type == type)?.Attribute.RawValueULong;
                    var log = new NvmeLog(
                        Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType.CriticalWarning),
                        Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType.MediaAndDataIntegrityErrors),
                        Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType.UnsafeShutdownCount));
                    if (log.CriticalWarning != null || log.MediaErrors != null) logs[hw.Name] = log;
                }
                catch (Exception ex) { DiagnosticLog.Write("sensors.nvme", ex); }
            }
        }
        return logs;
    }

    public SensorMonitor() : this(new LibreHardwareSource()) { }
    internal SensorMonitor(IHardwareSource source) => _source = source;

    public Task OpenAsync()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _openTask ??= Task.Run(() =>
            {
                try { _source.Open(); _networks = PhysicalNetwork.ActiveNames(); }
                catch
                {
                    CloseSource();
                    throw;
                }
                bool close;
                lock (_lifecycle) { close = _disposed; if (!close) _open = true; }
                if (close) CloseSource();
            });
        }
    }

    public void Refresh()
    {
        lock (_lifecycle)
        {
            if (!_open || _disposed || _paused) return;
            foreach (var hw in _source.Hardware)
            {
                if (_gpuRetired && SensorTypeMeta.IsGpu(hw.HardwareType)) continue;
                if (hw.HardwareType == HardwareType.Network && _networks != null && !_networks.Contains(hw.Name)) continue;
                string rootId = hw.Identifier.ToString();
                try { Walk(hw, hw); _failedHardware.Remove(rootId); }
                catch (Exception ex)
                {
                    if (_failedHardware.Add(rootId)) DiagnosticLog.Write("sensors.refresh", ex);
                    foreach (var row in Rows.Where(r => r.RootId == rootId || r.Hardware == hw.Name))
                        row.Update(null, null, null);
                }
            }
        }
    }

    private void Walk(IHardware hw, IHardware root)
    {
        hw.Update();
        bool inaccessibleCpu = hw.HardwareType == HardwareType.Cpu && !IsPawnIOInstalled
            && !hw.Sensors.Any(x => x.SensorType == SensorType.Temperature && x.Value is > 0);
        foreach (var s in hw.Sensors)
        {
            string id = s.Identifier.ToString();
            if (!_index.TryGetValue(id, out var row))
            {
                row = new SensorRow
                {
                    Id = id,
                    Hardware = hw.Name,
                    RootId = root.Identifier.ToString(),
                    RootName = root.Name,
                    RootType = root.HardwareType,
                    Category = SensorRow.CategoryName(s.SensorType),
                    Name = s.Name,
                    Type = s.SensorType,
                    HardwareType = hw.HardwareType
                };
                _index[id] = row;
                Rows.Add(row);
            }
            bool unavailable = inaccessibleCpu && s.SensorType is SensorType.Temperature or SensorType.Power or SensorType.Voltage or SensorType.Clock;
            row.Update(unavailable ? null : s.Value, unavailable ? null : s.Min, unavailable ? null : s.Max);
        }
        foreach (var sub in hw.SubHardware)
            Walk(sub, root);
    }

    public void ResetStats()
    {
        lock (_lifecycle)
            foreach (var row in Rows) row.ResetStats();
    }

    public float? CpuTemperature
    {
        get
        {
            lock (_lifecycle)
            {
                var temps = Rows.Where(r => r.HardwareType == HardwareType.Cpu
                                         && r.Type == SensorType.Temperature
                                         && r.RawValue is > 0).ToList();
                var main = temps.FirstOrDefault(r => r.Name.Contains("Package") || r.Name.Contains("Tctl") || r.Name.Contains("Tdie"));
                return main?.RawValue ?? temps.Max(r => r.RawValue);
            }
        }
    }

    public float? CpuLoad
    {
        get
        {
            lock (_lifecycle)
                return Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                             && r.Type == SensorType.Load
                                             && r.Name == "CPU Total")?.RawValue;
        }
    }

    public float? CpuPower
    {
        get
        {
            lock (_lifecycle)
                return Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                             && r.Type == SensorType.Power
                                             && r.Name.Contains("Package") && r.RawValue > 0)?.RawValue;
        }
    }

    /// <summary>
    /// Driver PawnIO (necessário para temperatura/consumo da CPU via MSR/SMN).
    /// A biblioteca procura exatamente esta chave de desinstalação; o resultado é cacheado para evitar abrir o Registro a cada tick.
    /// </summary>
    public static bool IsPawnIOInstalled => _pawnIoInstalled ??= ProbePawnIO();

    public static void RefreshPawnIOStatus() => _pawnIoInstalled = ProbePawnIO();

    private static bool ProbePawnIO()
    {
        try
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                     Microsoft.Win32.RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return key != null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            DiagnosticLog.Write("sensors.pawnio", ex);
            return false;
        }
    }

    public static bool IsElevated => ProcessElevation.IsElevated;

    public enum CpuSensorProblem { None, NotElevated, PawnIOMissing, ReadFailed }

    public CpuSensorProblem DiagnoseCpuSensors()
    {
        if (CpuTemperature != null) return CpuSensorProblem.None;
        if (!ProcessElevation.IsElevated) return CpuSensorProblem.NotElevated;
        if (!IsPawnIOInstalled) return CpuSensorProblem.PawnIOMissing;
        return CpuSensorProblem.ReadFailed;
    }

    public static string Explain(CpuSensorProblem p) => p switch
    {
        // A primeira frase vale sozinha (a Visão geral mostra só ela); sem citar outros programas.
        CpuSensorProblem.NotElevated =>
            "O Windows só libera os sensores internos do processador com o CoreBi aberto como administrador. " +
            "Feche e abra de novo aceitando o pedido de administrador.",
        CpuSensorProblem.PawnIOMissing =>
            "Falta o driver PawnIO, que lê a temperatura e o consumo do processador. Ele é gratuito e assinado. " +
            "Depois de instalar, feche e abra o CoreBi.",
        CpuSensorProblem.ReadFailed =>
            "O PawnIO está instalado, mas a leitura falhou. Se acabou de instalar, reinicie o PC. Se continuar, " +
            "confira se o antivírus bloqueou o driver ou se a biblioteca de sensores ainda não reconhece este processador.",
        _ => ""
    };

    public void Dispose()
    {
        bool close;
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            close = _open;
            _open = false;
        }
        if (close) CloseSource();
    }

    private void CloseSource()
    {
        try { _source.Close(); }
        catch (Exception ex) { DiagnosticLog.Write("sensors.close", ex); }
    }
}

public sealed record NvmeLog(ulong? CriticalWarning, ulong? MediaErrors, ulong? UnsafeShutdowns);

internal interface IHardwareSource
{
    IEnumerable<IHardware> Hardware { get; }
    void Open();
    void Close();
}

internal sealed class LibreHardwareSource : IHardwareSource
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = true,
        IsControllerEnabled = true
    };
    public IEnumerable<IHardware> Hardware => _computer.Hardware;
    public void Open() => _computer.Open();
    public void Close() => _computer.Close();
}
