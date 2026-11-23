using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using CoreBi.Engine.Infrastructure;
using Microsoft.Win32;

namespace CoreBi.Engine.Tools.Startup;

/// <summary>
/// Um início do Windows medido pelo próprio Windows (evento 100 do log Diagnostics-Performance). <see cref="MainPathMs"/> vai do
/// carregamento do Windows até a área de trabalho aparecer; <see cref="PostBootMs"/>, da área de trabalho até o PC ficar
/// ocioso, que é onde os programas de inicialização pesam. O total é a soma dos dois.
/// </summary>
public sealed record BootTime(DateTime When, double TotalMs, double MainPathMs, double PostBootMs, int StartupApps);

/// <summary>Último início e os anteriores, mais o tempo da BIOS no último (o Windows só guarda esse).</summary>
public sealed record BootReport(IReadOnlyList<BootTime> Boots, double? BiosMs, bool FastStartup, string? Unavailable)
{
    public BootTime? Last => Boots.Count > 0 ? Boots[0] : null;
}

public static class BootTimes
{
    private const string Log = "Microsoft-Windows-Diagnostics-Performance/Operational";

    /// <summary>Lê até <paramref name="count"/> inícios, mais recente primeiro. O log só abre como administrador.</summary>
    public static BootReport Read(int count = 10)
    {
        double? bios = null;
        bool fast = false;
        try
        {
            using var power = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            if (power?.GetValue("FwPOSTTime") is int post && post > 0 && post < 600_000) bios = post;
            fast = power?.GetValue("HiberbootEnabled") is int hiber && hiber == 1;
        }
        catch { }

        var boots = new List<BootTime>();
        try
        {
            var query = new EventLogQuery(Log, PathType.LogName, "*[System[EventID=100]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (EventRecord? record = reader.ReadEvent(); record != null && boots.Count < count; record = reader.ReadEvent())
            {
                using (record)
                {
                    if (Parse(record.ToXml(), record.TimeCreated) is { } boot) boots.Add(boot);
                }
            }
        }
        catch (UnauthorizedAccessException) { return new BootReport(boots, bios, fast, "admin"); }
        catch (EventLogNotFoundException) { return new BootReport(boots, bios, fast, "log"); }
        catch (Exception ex) { DiagnosticLog.Write("startup.boot-times", ex); return new BootReport(boots, bios, fast, "log"); }
        return new BootReport(boots, bios, fast, boots.Count == 0 ? "empty" : null);
    }

    internal static BootTime? Parse(string xml, DateTime? created)
    {
        var data = XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "Data")
            .ToDictionary(e => (string?)e.Attribute("Name") ?? "", e => e.Value, StringComparer.OrdinalIgnoreCase);
        double Ms(string name) => data.TryGetValue(name, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;
        double total = Ms("BootTime"), main = Ms("MainPathBootTime"), post = Ms("BootPostBootTime");
        if (total <= 0 || total > 3_600_000) return null;
        if (main <= 0 && post <= 0) main = total;
        DateTime when = created ?? DateTime.Now;
        if (data.TryGetValue("BootStartTime", out var start) && DateTime.TryParse(start, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var utc))
            when = utc.ToLocalTime();
        return new BootTime(when, total, main, post, (int)Ms("BootNumStartupApps"));
    }
}
