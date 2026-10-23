using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Games;
using LibreHardwareMonitor.Hardware;

internal static partial class Program
{
    private static void TelemetryTests()
    {
        Check("RAM: bytes and percent share the physical Windows memory denominator", () =>
        {
            const long gib = 1L << 30;
            var memory = MemoryInfo.PhysicalUsageFrom(32 * gib, (long)(7.6 * gib));
            Require(memory is { } value && Math.Abs(value.Used / (double)gib - 24.4) < 0.001
                && Math.Abs(value.UsedPercent - 76.25) < 0.001, "24.4 / 32 GB must be 76.25%, never 90%");
            Require(MemoryInfo.PhysicalUsageFrom(0, 0) == null && MemoryInfo.PhysicalUsageFrom(32, -1) == null
                && MemoryInfo.PhysicalUsageFrom(32, 33) == null, "Invalid RAM readings must be unavailable");
            Require(MemoryInfo.PhysicalUsageFrom(32, 32)?.UsedPercent == 0
                && MemoryInfo.PhysicalUsageFrom(32, 0)?.UsedPercent == 100, "Physical RAM boundary values");
            long[] capacitiesGiB = [4, 8, 16, 24, 32, 64, 96, 128, 1024];
            foreach (long capacity in capacitiesGiB)
            {
                long total = capacity * gib;
                var reading = MemoryInfo.PhysicalUsageFrom(total, total * 5 / 8);
                Require(reading?.UsedPercent == 37.5f && reading?.Used == total * 3 / 8,
                    $"Physical RAM calculation depends on a particular capacity: {capacity} GiB");
            }
            long usable = 32 * gib - 128 * 1024 * 1024;
            Require(MemoryInfo.PhysicalUsageFrom(usable, usable / 4)?.UsedPercent == 75,
                "Use Windows-available physical capacity, including hardware reservations");
            return new { usedGiB = memory!.Value.Used / (double)gib, memory.Value.UsedPercent, capacitiesGiB };
        });
        Check("RAM: duplicate physical and virtual sensor names cannot select virtual load", () =>
        {
            using var sensors = new SensorMonitor();
            foreach (var (hardware, value) in new[] { ("Virtual Memory", 90f), ("Total Memory", 76.25f) })
            {
                var row = new SensorRow { Hardware = hardware, HardwareType = HardwareType.Memory, Type = SensorType.Load, Name = "Memory" };
                row.Update(value, value, value); sensors.Rows.Add(row);
            }
            var sample = BottleneckAnalyzer.Sample(sensors, null, null, MemoryInfo.PhysicalUsageFrom(3200, 760));
            Require(sample.RamPct == 76.25f, "A sensor named Memory selected virtual commit load instead of physical RAM");
            Require(BottleneckAnalyzer.Sample(sensors, null, null, null).RamPct == null, "Missing physical memory must remain unavailable");
            return new { virtualPercent = 90, physicalPercent = sample.RamPct };
        });
        Check("GPU: busiest engine sums processes and isolates physical adapters", () =>
        {
            static string Name(int pid, uint low, int engine, int physical = 0)
                => $"pid_{pid}_luid_0xFEDCBA98_0x{low:X8}_phys_{physical}_eng_{engine}_engtype_3D";
            var loads = WindowsGpuUsage.Aggregate(new (string, double)[]
            {
                (Name(1, 7, 0), 55), (Name(2, 7, 0), 20), (Name(1, 7, 1), 40),
                (Name(3, 7, 0, 1), 10), (Name(1, 8, 0), 98), (Name(4, 9, 0), 120),
                (Name(5, 10, 0), 0), (Name(6, 7, 2), double.NaN), (Name(6, 7, 2), -10), ("invalid", 100)
            });
            Require(loads[0xFEDCBA9800000007] == 75, "GPU usage must be max(sum(processes per engine)), not max(process) or sum(engines)");
            Require(loads[0xFEDCBA9800000008] == 98 && loads[0xFEDCBA9800000009] == 100
                && loads[0xFEDCBA980000000A] == 0 && loads.Count == 4, "Adapter isolation, valid zero or invalid sample handling failed");
            Require(WindowsGpuUsage.Aggregate(Array.Empty<(string, double)>()).Count == 0, "Missing counters must not fabricate 0%");
            return new { primary = loads[0xFEDCBA9800000007], otherAdapter = loads[0xFEDCBA9800000008] };
        });
    }

    private static void OverlayTelemetry()
    {
        Check("telemetry: physical RAM and live Windows GPU counters", () =>
        {
            var gpus = GpuInfoCollector.Collect();
            using var usage = new WindowsGpuUsage();
            var samples = new List<object>();
            var warmup = System.Diagnostics.Stopwatch.StartNew();
            while (gpus.All(g => usage.GetLoad(g.AdapterLuid) == null) && warmup.Elapsed < TimeSpan.FromSeconds(12))
                Thread.Sleep(250);
            for (int i = 0; i < 4; i++)
            {
                Thread.Sleep(1100);
                var memory = MemoryInfo.ReadPhysicalUsage();
                Require(memory is { Total: > 0 }, "Windows physical RAM unavailable");
                var values = gpus.Select(g => new
                {
                    g.Name,
                    luid = g.AdapterLuid?.ToString("X16"),
                    windowsPercent = usage.GetLoad(g.AdapterLuid),
                    vendorPercent = g.NvmlHandle != IntPtr.Zero ? Nvml.Util(g.NvmlHandle)?.Gpu : null
                }).ToArray();
                samples.Add(new { memory, gpus = values });
                Require(values.Any(g => g.windowsPercent is >= 0 and <= 100), $"Windows GPU performance counters returned no data: {usage.Diagnostic}; adapters: {string.Join(", ", values.Select(g => g.luid))}");
            }
            return samples;
        });
    }
}
