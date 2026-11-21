using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Sensors;

public sealed record CpuLiveSample(
    float? LoadPct,
    float? BusiestCoreLoadPct,
    string? BusiestCoreLabel,
    float? TempC,
    float? PowerW,
    float? AvgClockMHz,
    float? MaxClockMHz);

/// <summary>
/// Telemetria ao vivo de CPU, para que painel, overlay, gargalo e stress usem exatamente a mesma leitura.
/// </summary>
public static class CpuTelemetry
{
    public static CpuLiveSample Read(SensorMonitor monitor, IEnumerable<SensorReading> sensors)
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
}
