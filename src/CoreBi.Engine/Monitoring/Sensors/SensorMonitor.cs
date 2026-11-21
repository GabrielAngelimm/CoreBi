using System.Collections.ObjectModel;
using CoreBi.Engine.Infrastructure;
using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Sensors;

/// <summary>Encapsula o LibreHardwareMonitorLib e mantém a lista de sensores atualizada.</summary>
public sealed partial class SensorMonitor : IDisposable
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
    private SensorReading[]? _snapshot; // cópia da última leitura; refeita só depois de Refresh, ResetStats ou mudança de linhas

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

    /// <summary>Volta a ler depois que os dispositivos assentaram. A placa de vídeo continua de fora se o driver dela mudou.</summary>
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
            _snapshot = null;
        }
    }

    public bool GpuRetired => _gpuRetired;

    /// <summary>
    /// Cópia imutável das leituras, para quem lê fora da coleção da interface (overlay, painéis, relatório).
    /// Várias páginas pedem a cópia no mesmo tique: ela é montada uma vez por leitura, não uma vez por pedido.
    /// </summary>
    public IReadOnlyList<SensorReading> Snapshot()
    {
        lock (_lifecycle)
            return _snapshot ??= Rows.Select(r => new SensorReading(r.Id, r.Hardware, r.Name, r.Type, r.HardwareType, r.RawValue, r.Category, r.Value, r.Min, r.Max)).ToArray();
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
    internal SensorMonitor(IHardwareSource source)
    {
        _source = source;
        Rows.CollectionChanged += (_, _) => _snapshot = null;
    }

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
            _snapshot = null;
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
        {
            foreach (var row in Rows) row.ResetStats();
            _snapshot = null;
        }
    }

    /// <summary>Temperatura principal da CPU: Package (Intel) / Tctl-Tdie (AMD), senão a maior entre os núcleos.</summary>
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
