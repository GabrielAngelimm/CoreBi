using LibreHardwareMonitor.Hardware;

using System.Collections.ObjectModel;

using CoreBi.Engine.Infrastructure;

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
    private HashSet<string>? _networks; // placas de rede físicas e ativas; as interfaces virtuais nem são lidas

    public ObservableCollection<SensorRow> Rows { get; } = new();
    public bool IsOpen => _open;
    public IReadOnlyList<SensorReading> Snapshot()
    {
        lock (_lifecycle)
            return Rows.Select(r => new SensorReading(r.Id, r.Hardware, r.Name, r.Type, r.HardwareType, r.RawValue, r.Category, r.Value, r.Min, r.Max)).ToArray();
    }

    /// <summary>
    /// Relatório de saúde do próprio SSD NVMe (aviso crítico, erros de mídia, desligamentos inesperados), por nome do disco.
    /// O Windows nem sempre repassa esses contadores; a biblioteca de sensores os lê direto do disco a cada atualização.
    /// </summary>
    public IReadOnlyDictionary<string, NvmeLog> NvmeLogs()
    {
        var logs = new Dictionary<string, NvmeLog>(StringComparer.Ordinal);
        lock (_lifecycle)
        {
            if (!_open || _disposed) return logs;
            foreach (var hw in _source.Hardware)
            {
                if (hw.HardwareType != HardwareType.Storage || hw is not LibreHardwareMonitor.Hardware.Storage.StorageDevice device) continue;
                try
                {
                    var smart = device.Storage?.Smart?.SmartAttributes;
                    if (smart == null) continue;
                    ulong? Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType type) =>
                        smart.FirstOrDefault(a => a.Info?.Type == type)?.Attribute.RawValueULong;
                    var log = new NvmeLog(Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType.CriticalWarning),
                        Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType.MediaAndDataIntegrityErrors),
                        Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType.UnsafeShutdownCount),
                        Raw(DiskInfoToolkit.Interop.Enums.SmartAttributeType.NumberOfErrorInformationLogEntries));
                    if (log.CriticalWarning != null || log.MediaErrors != null) logs[hw.Name] = log;
                }
                catch (Exception ex) { DiagnosticLog.Write("sensors.nvme", ex); }
            }
        }
        return logs;
    }

    public SensorMonitor() : this(new LibreHardwareSource()) { }
    internal SensorMonitor(IHardwareSource source) => _source = source;

    /// <summary>Detecta o hardware (leva de 0,5 a alguns segundos) fora da thread da janela. Até terminar, Refresh não faz nada.</summary>
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
            if (!_open || _disposed) return;
            foreach (var hw in _source.Hardware)
            {
                if (hw.HardwareType == HardwareType.Network && _networks != null && !_networks.Contains(hw.Name)) continue;
                try { Walk(hw, hw); _failedHardware.Remove(hw.Identifier.ToString()); }
                catch (Exception ex)
                {
                    if (_failedHardware.Add(hw.Identifier.ToString())) DiagnosticLog.Write("sensors.refresh", ex);
                    foreach (var row in Rows.Where(r => r.Hardware == hw.Name)) row.Update(null, null, null);
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

    /// <summary>Recomeça mínimo, média e máximo de todos os sensores a partir da leitura atual.</summary>
    public void ResetStats()
    {
        lock (_lifecycle)
            foreach (var row in Rows) row.ResetStats();
    }

    /// <summary>Temperatura principal da CPU: Package (Intel) / Tctl-Tdie (AMD), senão a maior entre os núcleos.</summary>
    public float? CpuTemperature
    {
        get
        {
            var temps = Rows.Where(r => r.HardwareType == HardwareType.Cpu
                                     && r.Type == SensorType.Temperature
                                     && r.RawValue is > 0).ToList();
            var main = temps.FirstOrDefault(r => r.Name.Contains("Package") || r.Name.Contains("Tctl") || r.Name.Contains("Tdie"));
            return main?.RawValue ?? temps.Max(r => r.RawValue);
        }
    }

    /// <summary>Carga total da CPU em %.</summary>
    public float? CpuLoad => Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                                   && r.Type == SensorType.Load
                                                   && r.Name == "CPU Total")?.RawValue;

    /// <summary>Consumo do pacote da CPU em W.</summary>
    public float? CpuPower => Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                                    && r.Type == SensorType.Power
                                                    && r.Name.Contains("Package") && r.RawValue > 0)?.RawValue;

    /// <summary>
    /// Driver PawnIO (necessário para temperatura/consumo da CPU via MSR/SMN).
    /// A biblioteca procura exatamente esta chave de desinstalação.
    /// </summary>
    public static bool IsPawnIOInstalled
    {
        get
        {
            try
            {
                using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                         Microsoft.Win32.RegistryView.Registry64);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                return key != null;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { DiagnosticLog.Write("sensors.pawnio", ex); return false; }
        }
    }

    public static bool IsElevated =>
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    public enum CpuSensorProblem { None, NotElevated, PawnIOMissing, ReadFailed }

    /// <summary>Por que a temperatura/consumo da CPU não aparecem (o motivo mais provável primeiro).</summary>
    public CpuSensorProblem DiagnoseCpuSensors()
    {
        if (CpuTemperature != null) return CpuSensorProblem.None;
        if (!IsElevated) return CpuSensorProblem.NotElevated;
        if (!IsPawnIOInstalled) return CpuSensorProblem.PawnIOMissing;
        return CpuSensorProblem.ReadFailed;
    }

    public static string Explain(CpuSensorProblem p) => p switch
    {
        CpuSensorProblem.NotElevated =>
            "O CoreBi não está rodando como administrador, e o Windows só libera os sensores internos da CPU " +
            "nesse modo. Feche e abra de novo aceitando o pedido de administrador.",
        CpuSensorProblem.PawnIOMissing =>
            "Falta o driver PawnIO, que lê a temperatura e o consumo da CPU. Ele é gratuito, assinado e usado também " +
            "pelo LibreHardwareMonitor e pelo FanControl. Depois de instalar, feche e abra o CoreBi.",
        CpuSensorProblem.ReadFailed =>
            "O PawnIO está instalado, mas a leitura falhou. Se acabou de instalar, reinicie o PC. Se continuar, " +
            "confira se o antivírus não bloqueou o driver ou se a CPU é muito antiga/recente para a biblioteca de sensores.",
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

/// <summary>Campos do log de saúde NVMe; null quando o disco não traz o campo.</summary>
public sealed record NvmeLog(ulong? CriticalWarning, ulong? MediaErrors, ulong? UnsafeShutdowns, ulong? ErrorLogEntries);

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
