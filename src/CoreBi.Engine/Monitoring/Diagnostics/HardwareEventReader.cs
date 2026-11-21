namespace CoreBi.Engine.Monitoring.Diagnostics;

public sealed record HardwareErrors(int Whea, int GpuResets, string? Error)
{
    public int Total => Whea + GpuResets;
}

/// <summary>Read-only WHEA and display-driver event diagnostics.</summary>
public static class HardwareEventReader
{
    public static HardwareErrors ErrorsSince(DateTime since)
    {
        string utc = since.ToUniversalTime().ToString("o");
        try
        {
            int whea = EventLogReaderHelper.Count("System",
                $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[@SystemTime>='{utc}']]]");
            int gpu = EventLogReaderHelper.Count("System",
                $"*[System[((Provider[@Name='nvlddmkm'] and (EventID=13 or EventID=153)) or (Provider[@Name='Display'] and EventID=4101) or (Provider[@Name='amdkmdag'] and (Level=2 or Level=3))) and TimeCreated[@SystemTime>='{utc}']]]");
            return new HardwareErrors(whea, gpu, null);
        }
        catch (Exception ex)
        {
            return new HardwareErrors(0, 0, ex.Message);
        }
    }
}
