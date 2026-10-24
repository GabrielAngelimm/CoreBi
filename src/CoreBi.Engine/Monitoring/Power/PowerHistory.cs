using System.Diagnostics.Eventing.Reader;

namespace CoreBi.Engine.Monitoring.Power;

/// <summary>Um trecho em que o PC esteve ligado; Current marca a sessão de agora (vai até este instante).</summary>
public sealed record OnSpan(DateTime From, DateTime To, bool Current);

/// <summary>
/// Quando o PC ficou ligado nos últimos dias, reconstruído do registro de eventos do Windows: partidas, desligamentos,
/// suspensões e retornos. Desligamentos inesperados (Kernel-Power 41) aparecem à parte, na hora da partida seguinte.
/// </summary>
public sealed record PowerHistory(
    DateTime Since, DateTime? LogStart, IReadOnlyList<OnSpan> Spans, IReadOnlyList<DateTime> Unexpected, string? Error)
{
    /// <summary>Tempo ligado dentro de um dia (00:00 a 24:00 locais).</summary>
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

    /// <summary>O registro cobre o dia inteiro? (o Windows descarta eventos antigos quando o log enche)</summary>
    public bool Covers(DateTime day) => LogStart is DateTime start && start <= day.Date;
}

public static class PowerHistoryReader
{
    private enum Kind { Boot, Resume, Off, Crash, Wake }

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
            var events = new List<(DateTime At, Kind Kind)>();
            using (var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath)))
            {
                for (EventRecord? rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
                {
                    using (rec)
                    {
                        if (rec.TimeCreated is not DateTime at) continue;
                        Kind? kind = (rec.ProviderName, rec.Id) switch
                        {
                            ("Microsoft-Windows-Kernel-General", 12) => Kind.Boot,
                            ("Microsoft-Windows-Kernel-General", 13) => Kind.Off,
                            ("Microsoft-Windows-Kernel-Power", 41) => Kind.Crash,
                            ("Microsoft-Windows-Kernel-Power", 107 or 507) => Kind.Resume,
                            ("Microsoft-Windows-Kernel-Power", 42 or 109 or 506) => Kind.Off,
                            ("Microsoft-Windows-Power-Troubleshooter", 1) => Kind.Wake,
                            _ => null,
                        };
                        if (kind is Kind k) events.Add((at, k));
                    }
                }
            }
            events.Sort((a, b) => a.At.CompareTo(b.At));

            var spans = new List<OnSpan>();
            var crashes = new List<DateTime>();
            DateTime? on = null;
            foreach (var (at, kind) in events)
            {
                switch (kind)
                {
                    case Kind.Boot:
                        // Partida nova: um trecho ainda aberto terminou sem registro (queda) e fica de fora
                        on = at;
                        break;
                    case Kind.Resume:
                        on ??= at; // retorno da suspensão (e da inicialização rápida); repetido no mesmo retorno, vale o primeiro
                        break;
                    case Kind.Off:
                        if (on is DateTime from && at > from) spans.Add(new OnSpan(from, at, false));
                        on = null;
                        break;
                    case Kind.Crash:
                        // Registrado logo depois da partida que seguiu a queda: o PC está ligado desde essa partida
                        crashes.Add(at);
                        on ??= at;
                        break;
                    case Kind.Wake:
                        on ??= at; // acordou da suspensão (às vezes vem antes do retorno do Kernel-Power)
                        break;
                }
            }
            // A sessão atual: do último "ligou" até agora; sem evento de abertura (log muito curto), não inventa o início
            if (on is DateTime open) spans.Add(new OnSpan(open, now, true));

            return new PowerHistory(since, LogStart(), spans, crashes.Where(c => c >= since).ToList(), null);
        }
        catch (Exception ex)
        {
            return new PowerHistory(since, null, Array.Empty<OnSpan>(), Array.Empty<DateTime>(), ex.Message);
        }
    }

    /// <summary>Evento mais antigo ainda guardado no log System.</summary>
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
