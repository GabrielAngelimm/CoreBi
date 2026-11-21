using System.Diagnostics.Eventing.Reader;
using CoreBi.Engine.Monitoring.Diagnostics;

namespace CoreBi.Engine.Monitoring.Power;

public sealed record OnSpan(DateTime From, DateTime To, bool Current);

/// <summary>
/// Períodos em que o PC ficou ligado nos últimos dias, reconstruídos do log System do Windows.
/// Desligamentos inesperados (Kernel-Power 41) aparecem à parte na hora da partida seguinte.
/// </summary>
public sealed record PowerHistory(
    DateTime Since, DateTime? LogStart, IReadOnlyList<OnSpan> Spans, IReadOnlyList<DateTime> Unexpected, string? Error)
{
    public TimeSpan OnTime(DateTime day)
    {
        DateTime a = day.Date, b = a.AddDays(1);
        var total = TimeSpan.Zero;
        foreach (var s in Spans)
        {
            var from = s.From > a ? s.From : a;
            var to = s.To < b ? s.To : b;
            if (to > from) total += to - from;
        }
        return total;
    }

    public bool Covers(DateTime day) => LogStart is DateTime start && start <= day.Date;
}

internal enum PowerEventKind { Boot, Resume, Off, Crash }

public static class PowerHistoryReader
{
    public static PowerHistory Read(int days = 7)
    {
        DateTime now = DateTime.Now, since = now.Date.AddDays(1 - days);
        try
        {
            string t = since.AddDays(-1).ToUniversalTime().ToString("o"); // um dia antes, para saber o estado à meia-noite
            string xpath = "*[System[(" +
                "(Provider[@Name='Microsoft-Windows-Kernel-General'] and (EventID=12 or EventID=13)) or " +
                "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41 or EventID=42 or EventID=107 or EventID=109 or EventID=506 or EventID=507)) or " +
                "(Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1)" +
                $") and TimeCreated[@SystemTime>='{t}']]]";
            var events = new List<(DateTime At, PowerEventKind Kind)>();
            EventLogReaderHelper.ForEach("System", xpath, rec =>
            {
                if (rec.TimeCreated is not DateTime at) return;
                PowerEventKind? kind = (rec.ProviderName, rec.Id) switch
                {
                    ("Microsoft-Windows-Kernel-General", 12) => PowerEventKind.Boot,
                    ("Microsoft-Windows-Kernel-General", 13) => PowerEventKind.Off,
                    ("Microsoft-Windows-Kernel-Power", 41) => PowerEventKind.Crash,
                    ("Microsoft-Windows-Kernel-Power", 107 or 507) => PowerEventKind.Resume,
                    ("Microsoft-Windows-Kernel-Power", 42 or 109 or 506) => PowerEventKind.Off,
                    ("Microsoft-Windows-Power-Troubleshooter", 1) => PowerEventKind.Resume,
                    _ => null,
                };
                if (kind is PowerEventKind k) events.Add((at, k));
            });

            return Build(since, now, LogStart(), events);
        }
        catch (Exception ex)
        {
            return new PowerHistory(since, null, Array.Empty<OnSpan>(), Array.Empty<DateTime>(), ex.Message);
        }
    }

    internal static PowerHistory Build(DateTime since, DateTime now, DateTime? logStart, IEnumerable<(DateTime At, PowerEventKind Kind)> rawEvents)
    {
        var events = rawEvents.OrderBy(e => e.At).ToList();
        var spans = new List<OnSpan>();
        var crashes = new List<DateTime>();
        DateTime? on = null;
        foreach (var (at, kind) in events)
        {
            switch (kind)
            {
                case PowerEventKind.Boot:
                    // Partida nova: um trecho ainda aberto terminou sem registro (queda) e fica de fora.
                    on = at;
                    break;
                case PowerEventKind.Resume:
                    on ??= at; // retorno da suspensão / Power-Troubleshooter; repetido no mesmo retorno, vale o primeiro
                    break;
                case PowerEventKind.Off:
                    if (on is DateTime from && at > from) spans.Add(new OnSpan(from, at, false));
                    on = null;
                    break;
                case PowerEventKind.Crash:
                    // Registrado logo depois da partida que seguiu a queda: o PC está ligado desde essa partida.
                    crashes.Add(at);
                    on ??= at;
                    break;
            }
        }
        // A sessão atual: do último "ligou" até agora; sem evento de abertura (log muito curto), não inventa o início.
        if (on is DateTime open) spans.Add(new OnSpan(open, now, true));
        return new PowerHistory(since, logStart, spans, crashes.Where(c => c >= since).ToList(), null);
    }

    private static DateTime? LogStart()
    {
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, "*"));
            using var rec = reader.ReadEvent();
            return rec?.TimeCreated;
        }
        catch { return null; }
    }
}
