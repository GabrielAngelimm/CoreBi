using System.Diagnostics.Eventing.Reader;
namespace CoreBi.Engine.Monitoring.Diagnostics;

/// <summary>Read-only WHEA and display-driver event diagnostics.</summary>
public static class HardwareEventReader
{    /// <summary>Conta erros WHEA (CPU/RAM/PCIe) e reinícios do driver de vídeo desde o instante informado.</summary>
    public static HardwareErrors ErrorsSince(DateTime since)
    {
        string t = since.ToUniversalTime().ToString("o");
        try
        {
            int whea = Count($"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[@SystemTime>='{t}']]]", out string? lastWhea);
            int gpu = Count($"*[System[((Provider[@Name='nvlddmkm'] and (EventID=13 or EventID=153)) or (Provider[@Name='Display'] and EventID=4101) or Provider[@Name='amdkmdag']) and TimeCreated[@SystemTime>='{t}']]]", out string? lastGpu);
            return new HardwareErrors(whea, gpu, lastWhea ?? lastGpu, null);
        }
        catch (Exception ex)
        {
            return new HardwareErrors(0, 0, null, ex.Message);
        }
    }

    private static int Count(string xpath, out string? last)
    {
        last = null;
        int n = 0;
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true });
        for (EventRecord? rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
        {
            using (rec)
            {
                if (n == 0)
                {
                    string text;
                    try { text = rec.FormatDescription() ?? ""; } catch { text = ""; }
                    last = $"{rec.TimeCreated:dd/MM HH:mm} · {rec.ProviderName} {rec.Id}"
                         + (text.Length > 0 ? " · " + text.Split('\n')[0].Trim() : "");
                }
                n++;
            }
        }
        return n;
    }

}
