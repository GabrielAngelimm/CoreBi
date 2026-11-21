using CoreBi.Engine.Monitoring.Sensors;
using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Graphics;

public sealed record GpuLiveSample(
    float? LoadPct,
    float? CoreClockMHz,
    float? MemoryClockMHz,
    float? TempC,
    float? HotspotTempC,
    float? PowerW,
    float? PowerLimitW,
    float? VoltageV,
    float? FanPct,
    float? FanRpm,
    double? VramUsedGb,
    double? VramTotalGb,
    double? VramPct,
    bool ThermalThrottled);

public sealed record CpuLiveSample(
    float? LoadPct,
    float? BusiestCoreLoadPct,
    string? BusiestCoreLabel,
    float? TempC,
    float? PowerW,
    float? AvgClockMHz,
    float? MaxClockMHz);

/// <summary>
/// Centraliza a telemetria ao vivo de GPU (WDDM PDH + NVML + fallback LibreHardwareMonitor) e CPU,
/// garantindo que painel, overlay, gargalo, placa de vídeo e stress usem exatamente a mesma leitura.
/// </summary>
public sealed class GpuMonitor : IDisposable
{
    private readonly WindowsGpuUsage _windowsUsage = new();

    public void UpdateLoads(IEnumerable<GpuInfo> gpus)
    {
        foreach (var gpu in gpus)
            gpu.WindowsLoad = _windowsUsage.GetLoad(gpu.AdapterLuid);
    }

    public static GpuLiveSample ReadLive(GpuInfo gpu, IEnumerable<SensorReading> sensors)
    {
        var h = gpu.NvmlHandle;
        bool nvml = h != IntPtr.Zero;
        var gpuSensors = sensors.Where(r => r.Hardware == gpu.Name && r.RawValue != null).ToList();
        SensorReading? Row(SensorType type, string name) =>
            gpuSensors.FirstOrDefault(r => r.Type == type && r.Name == name);
        SensorReading? Any(SensorType type) => gpuSensors.FirstOrDefault(r => r.Type == type);

        float? load = gpu.WindowsLoad;
        float? coreClock = nvml && Nvml.Clock(h, Nvml.ClockGraphics) is uint cc
            ? cc
            : Row(SensorType.Clock, "GPU Core")?.RawValue is > 0 and float fc ? fc : null;
        float? memClock = nvml && Nvml.Clock(h, Nvml.ClockMem) is uint mc
            ? mc
            : Row(SensorType.Clock, "GPU Memory")?.RawValue is > 0 and float fm ? fm : null;

        float? temp = (Row(SensorType.Temperature, "GPU Core") ?? Any(SensorType.Temperature))?.RawValue;
        float? hotspot = Row(SensorType.Temperature, "GPU Hot Spot")?.RawValue;

        float? power = nvml && Nvml.PowerUsageMw(h) is uint mw
            ? mw / 1000f
            : Any(SensorType.Power)?.RawValue;
        float? powerLimit = nvml && Nvml.PowerLimitMw(h) is uint lim && lim > 0 ? lim / 1000f : null;

        float? voltage = Row(SensorType.Voltage, "GPU Core Voltage")?.RawValue
            ?? Row(SensorType.Voltage, "GPU Core")?.RawValue
            ?? Any(SensorType.Voltage)?.RawValue;
        float? fanPct = gpuSensors.FirstOrDefault(r => r.Type == SensorType.Control && r.Name.StartsWith("GPU Fan", StringComparison.Ordinal))?.RawValue
            ?? gpuSensors.FirstOrDefault(r => r.Type == SensorType.Control)?.RawValue;
        float? fanRpm = gpuSensors.FirstOrDefault(r => r.Type == SensorType.Fan && r.Name.StartsWith("GPU Fan", StringComparison.Ordinal) && r.RawValue > 0)?.RawValue
            ?? gpuSensors.FirstOrDefault(r => r.Type == SensorType.Fan && r.RawValue > 0)?.RawValue;

        double? vramUsedGb = null, vramTotalGb = null, vramPct = null;
        if (nvml && Nvml.MemoryInfo(h) is Nvml.Memory nm && nm.Total > 0)
        {
            vramUsedGb = nm.Used / 1073741824.0;
            vramTotalGb = nm.Total / 1073741824.0;
            vramPct = 100.0 * nm.Used / nm.Total;
        }
        else if (gpuSensors.FirstOrDefault(r => r.Name == "GPU Memory Used")?.RawValue is float usedMb
              && gpuSensors.FirstOrDefault(r => r.Name == "GPU Memory Total")?.RawValue is > 0 and float totalMb)
        {
            vramUsedGb = usedMb / 1024.0;
            vramTotalGb = totalMb / 1024.0;
            vramPct = 100.0 * usedMb / totalMb;
        }

        bool thermal = nvml && Nvml.ThrottleReasons(h) is ulong reasons && (reasons & Nvml.ThermalSlowdownMask) != 0;

        return new GpuLiveSample(load, coreClock, memClock, temp, hotspot, power, powerLimit, voltage, fanPct, fanRpm,
            vramUsedGb, vramTotalGb, vramPct, thermal);
    }

    public static CpuLiveSample ReadCpu(SensorMonitor monitor, IEnumerable<SensorReading> sensors)
    {
        var cpuSensors = sensors.Where(r => r.HardwareType == HardwareType.Cpu && r.RawValue != null).ToList();
        var busiest = cpuSensors
            .Where(r => r.Type == SensorType.Load && r.Name.StartsWith("CPU Core #", StringComparison.Ordinal) && !r.Name.Contains("Thread", StringComparison.Ordinal))
            .OrderByDescending(r => r.RawValue)
            .FirstOrDefault();

        var clocks = cpuSensors
            .Where(r => r.Type == SensorType.Clock
                     && r.Name.Contains("Core #", StringComparison.Ordinal)
                     && !r.Name.Contains("Effective", StringComparison.Ordinal)
                     && r.RawValue > 0)
            .Select(r => r.RawValue!.Value)
            .ToList();

        float? avgClock = clocks.Count > 0 ? clocks.Average() : null;
        float? maxClock = clocks.Count > 0 ? clocks.Max() : null;

        return new CpuLiveSample(
            monitor.CpuLoad,
            busiest?.RawValue,
            busiest?.Name.Replace("CPU Core #", "núcleo "),
            monitor.CpuTemperature,
            monitor.CpuPower,
            avgClock,
            maxClock);
    }

    public void Dispose() => _windowsUsage.Dispose();
}
