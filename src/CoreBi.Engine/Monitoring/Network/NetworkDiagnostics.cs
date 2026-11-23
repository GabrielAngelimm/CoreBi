using CoreBi.Engine.Monitoring.Health;

namespace CoreBi.Engine.Monitoring.Network;

/// <summary>Limites do veredito da internet e prefixo do achado informativo, compartilhados com a apresentação.</summary>
public static class NetworkThresholds
{
    public const double InternetLossWarnPct = 1;
    public const double InternetLossCritPct = 5;
    public const double InternetLatencyWarnMs = 80;
    public const double InternetLatencyCritMs = 150;
    public const double InternetJitterWarnMs = 20;
    /// <summary>Quanto a latência pode subir com a conexão cheia antes de o veredito apontar bufferbloat.</summary>
    public const double BufferbloatWarnMs = 100;

    /// <summary>Início do achado que não afeta o veredito (roteador que bloqueia ping).</summary>
    public const string RouterSilentNotePrefix = "O roteador não responde";
}

internal static class NetworkDiagnostics
{
    private sealed record Note(string Text, bool AffectsVerdict);

    internal sealed record Summary(
        HealthVerdict Overall,
        string Label,
        HealthVerdict LocalVerdict,
        HealthVerdict InternetVerdict,
        double? LatencyMs,
        double? JitterMs,
        double? LossPct,
        double? BufferbloatMs,
        IReadOnlyList<string> Notes);

    public static Summary Analyze(
        PingResult router,
        IReadOnlyList<PingResult> internetTargets,
        PingResult loadedBaselineTarget,
        double? dnsMs,
        double? downMbps,
        double? upMbps,
        string? downError,
        string? upError,
        bool upIncomplete,
        string? providerError,
        double? loadedMs,
        DateTimeOffset? limitedUntil,
        bool rateLimited)
    {
        var answeredInternet = internetTargets.Where(p => p.Answered).ToList();
        double? netAvg = answeredInternet.Count > 0 ? answeredInternet.Average(p => p.Avg) : null;
        double? netLoss = InternetLoss(internetTargets);
        double netJit = answeredInternet.Count > 0 ? answeredInternet.Max(p => p.Jitter) : 0;

        var notes = new List<Note>();
        if (limitedUntil is { } until) notes.Add(new(RateLimitReason(until), true));
        bool critical = false;

        bool lanBad = router.Answered && (router.LossPct > 2 || router.Avg > 30 || router.Jitter > 15);
        if (lanBad)
            notes.Add(new($"A rede local está instável (roteador: {router.Avg:0} ms, perda {router.LossPct:0.#}%, jitter {router.Jitter:0} ms). Se for Wi-Fi, aproxime-se do roteador ou use cabo.", true));
        if (!router.Answered)
            notes.Add(new(NetworkThresholds.RouterSilentNotePrefix + " a ping (alguns modelos bloqueiam). Isso por si só não é problema.", false));

        var internetVerdict = HealthVerdict.Good;
        if (answeredInternet.Count == 0)
        {
            critical = !HasHttpEvidence(downMbps, upMbps) && !rateLimited;
            internetVerdict = critical ? HealthVerdict.Critical : HealthVerdict.Unknown;
            notes.Add(new(critical
                ? "Sem resposta ICMP ou HTTP dos alvos testados; verifique a conexão."
                : "HTTP funcionou, mas os alvos não responderam a ICMP. A latência e a perda até esses alvos são inconclusivas.", true));
        }
        else
        {
            if (answeredInternet.Count < internetTargets.Count)
            {
                notes.Add(new("Um dos alvos de internet não respondeu a ping; a perda nesse alvo é inconclusiva.", true));
                internetVerdict = HealthVerdict.Warning;
            }
            if (netLoss > NetworkThresholds.InternetLossCritPct || netAvg > NetworkThresholds.InternetLatencyCritMs) { critical = true; internetVerdict = HealthVerdict.Critical; }
            else if (netLoss > NetworkThresholds.InternetLossWarnPct || netAvg > NetworkThresholds.InternetLatencyWarnMs || netJit > NetworkThresholds.InternetJitterWarnMs) internetVerdict = HealthVerdict.Warning;
            if (netLoss > NetworkThresholds.InternetLossWarnPct) notes.Add(new($"Há perda de pacotes nos alvos que responderam ({netLoss:0.#}%). Verifique a rede local e, se persistir, o provedor.", true));
            if (netAvg > NetworkThresholds.InternetLatencyWarnMs) notes.Add(new($"Latência alta até os alvos de internet ({netAvg:0} ms). O teste não identifica sozinho a origem do atraso.", true));
            if (netJit > NetworkThresholds.InternetJitterWarnMs) notes.Add(new($"O jitter está alto ({netJit:0} ms): a latência oscila, o que causa engasgos em jogos online.", true));
        }

        double? bloat = Bufferbloat(loadedBaselineTarget, loadedMs);
        if (bloat > NetworkThresholds.BufferbloatWarnMs)
            notes.Add(new($"A latência sobe para {loadedMs:0} ms quando a conexão está cheia (bufferbloat). Ative SQM/QoS no roteador ou limite a velocidade a ~90%.", true));
        if (dnsMs > 150)
            notes.Add(new($"A resolução do nome demorou {dnsMs:0} ms. O resultado pode incluir cache do Windows e atrasos da rede.", true));
        if (downMbps is < 10)
            notes.Add(new($"Download baixo ({downMbps:0.#} Mbps) para jogos, streaming e atualizações.", true));
        if (limitedUntil == null)
        {
            if (downMbps == null && upMbps == null) notes.Add(new(providerError ?? $"Não foi possível medir a velocidade{Reason(downError ?? upError)}.", true));
            else if (downMbps == null) notes.Add(new($"Não foi possível medir o download{Reason(downError ?? providerError)}.", true));
            else if (upMbps == null) notes.Add(new($"Não foi possível medir o upload{Reason(upError ?? providerError)}.", true));
        }
        if (downMbps != null && downError != null) notes.Add(new("Houve falhas durante o download; o resultado pode estar impreciso.", true));
        if (upMbps != null && (upIncomplete || upError != null)) notes.Add(new("Houve falhas ou envios incompletos no upload; o resultado pode estar impreciso.", true));

        var overall = critical ? HealthVerdict.Critical
            : notes.Any(n => n.AffectsVerdict) ? HealthVerdict.Warning
            : HealthVerdict.Good;
        string label = overall switch
        {
            HealthVerdict.Critical => "RUIM",
            HealthVerdict.Warning => "ATENÇÃO",
            _ => "ESTÁVEL"
        };
        string stable = bloat != null
            ? "Latência baixa, sem perda nos alvos testados e sem sinais de bufferbloat durante o download."
            : "Latência baixa e sem perda nos alvos testados; a comparação da latência sob carga ficou inconclusiva.";
        var finalNotes = notes.Count > 0 ? notes.Select(n => n.Text).ToList() : new List<string> { stable };
        var localVerdict = !router.Answered ? HealthVerdict.Unknown : lanBad ? HealthVerdict.Warning : HealthVerdict.Good;

        return new Summary(
            overall,
            label,
            localVerdict,
            internetVerdict,
            netAvg,
            answeredInternet.Count > 0 ? netJit : null,
            netLoss,
            bloat,
            finalNotes);
    }

    public static string RateLimitReason(DateTimeOffset until) =>
        $"O servidor da Cloudflare pediu uma pausa por excesso de requisições. Tente novamente após {until.LocalDateTime:dd/MM 'às' HH:mm:ss}.";

    internal static bool HasHttpEvidence(double? download, double? upload) => download is > 0 || upload is > 0;

    internal static double? InternetLoss(IEnumerable<PingResult> targets)
    {
        var answered = targets.Where(p => p.Answered).ToList();
        return answered.Count > 0 ? answered.Average(p => p.LossPct) : null;
    }

    internal static double? LoadedLatency(double? download, IReadOnlyList<double> samples) =>
        download is > 0 && samples.Count >= 5 ? samples.Average() : null;

    internal static double? Bufferbloat(PingResult baseline, double? loadedLatency) =>
        baseline.Answered && loadedLatency != null ? loadedLatency - baseline.Avg : null;

    private static string Reason(string? error) => string.IsNullOrEmpty(error) ? "" : $" ({error})";
}
