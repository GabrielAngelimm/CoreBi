using CoreBi.Engine.Monitoring.Health;

namespace CoreBi.Engine.Monitoring.Network;

/// <summary>Os pings de um alvo como foram medidos (null = pacote perdido), para refazer o traço e as médias.</summary>
public sealed record NetworkPingRun(string Name, string Address, List<double?> Samples);

/// <summary>
/// Um teste de rede concluído, guardado em AppSettings.NetRuns para a página abrir com o último resultado e comparar com os
/// anteriores. Leva só o que a página mostra: números, vereditos, achados e as amostras dos pings; nada de IP público,
/// endereço físico ou nome do adaptador.
/// </summary>
public sealed record NetworkRun
{
    public DateTime Date { get; init; }
    /// <summary>"Ethernet (cabo) · Cloudflare": o tipo da conexão e o servidor de velocidade.</summary>
    public string Source { get; init; } = "";
    public double? DownloadMbps { get; init; }
    public double? UploadMbps { get; init; }
    public bool UploadIncludesProtocol { get; init; }
    public double? LatencyMs { get; init; }
    public double? JitterMs { get; init; }
    public double? LossPct { get; init; }
    public double? DnsMs { get; init; }
    public double? LoadedLatencyMs { get; init; }
    public double? BufferbloatMs { get; init; }
    public HealthVerdict Verdict { get; init; } = HealthVerdict.Unknown;
    public string? Label { get; init; }
    public HealthVerdict LocalVerdict { get; init; } = HealthVerdict.Unknown;
    public HealthVerdict InternetVerdict { get; init; } = HealthVerdict.Unknown;
    public List<string> Notes { get; init; } = [];
    public NetworkPingRun? Router { get; init; }
    public List<NetworkPingRun> Internet { get; init; } = [];
    public List<double?> LoadedSamples { get; init; } = [];
}

public static class NetworkHistory
{
    /// <summary>Quantos testes ficam guardados (mais recente primeiro).</summary>
    public const int Capacity = 30;

    /// <summary>Só entra no histórico um teste que chegou ao fim, com conexão: incompleto ou sem rede não compara com nada.</summary>
    public static bool Keeps(NetworkResult result) =>
        result.Adapter != null && result.InterruptedPhase == null && result.RetryAfterUtc == null
        && (result.DownloadMbps != null || result.UploadMbps != null || result.LatencyMs != null);

    public static NetworkRun ToRun(NetworkResult r)
    {
        // O Meta do motor é "tipo · servidor · testado às hh:mm:ss"; a hora fica no Date
        string source = string.Join(" · ", r.Card.Meta.Split(" · ").Where(p => !p.StartsWith("testado", StringComparison.Ordinal)));
        return new NetworkRun
        {
            Date = r.TestedAt,
            Source = source,
            DownloadMbps = r.DownloadMbps,
            UploadMbps = r.UploadMbps,
            UploadIncludesProtocol = r.UploadIncludesProtocol,
            LatencyMs = r.LatencyMs,
            JitterMs = r.JitterMs,
            LossPct = r.LossPct,
            DnsMs = r.DnsMs,
            LoadedLatencyMs = r.LoadedLatencyMs,
            BufferbloatMs = r.BufferbloatMs,
            Verdict = r.Card.Verdict,
            Label = r.Card.Label,
            LocalVerdict = r.LocalVerdict,
            InternetVerdict = r.InternetVerdict,
            Notes = r.Notes.ToList(),
            Router = r.Router is { } router ? Ping(router) : null,
            Internet = r.Internet.Select(Ping).ToList(),
            LoadedSamples = r.LoadedSamples.ToList(),
        };
    }

    /// <summary>O resultado que a página desenha, refeito do que foi guardado (sem adaptador: a ficha da conexão é a de agora).</summary>
    public static NetworkResult ToResult(NetworkRun run) => new()
    {
        Card = new HealthCard
        {
            Title = "Diagnóstico de rede",
            Meta = run.Source,
            Verdict = run.Verdict,
            Label = run.Label,
            Reasons = run.Notes.ToList(),
        },
        Router = run.Router is { } router ? new PingResult(router.Name, router.Address, router.Samples) : null,
        Internet = run.Internet.Select(p => new PingResult(p.Name, p.Address, p.Samples)).ToArray(),
        LatencyMs = run.LatencyMs,
        JitterMs = run.JitterMs,
        LossPct = run.LossPct,
        DnsMs = run.DnsMs,
        DownloadMbps = run.DownloadMbps,
        UploadMbps = run.UploadMbps,
        UploadIncludesProtocol = run.UploadIncludesProtocol,
        LoadedSamples = run.LoadedSamples.ToArray(),
        LoadedLatencyMs = run.LoadedLatencyMs,
        BufferbloatMs = run.BufferbloatMs,
        LocalVerdict = run.LocalVerdict,
        InternetVerdict = run.InternetVerdict,
        Notes = run.Notes.ToArray(),
        TestedAt = run.Date,
    };

    /// <summary>Põe o teste no topo e corta o excesso pelo fim.</summary>
    public static void Add(List<NetworkRun> runs, NetworkRun run)
    {
        runs.Insert(0, run);
        if (runs.Count > Capacity) runs.RemoveRange(Capacity, runs.Count - Capacity);
    }

    /// <summary>Descarta entradas inválidas de um arquivo editado à mão ou de outra versão.</summary>
    public static List<NetworkRun> Normalize(List<NetworkRun>? runs) =>
        (runs ?? [])
            .Where(r => r != null && r.Date > DateTime.MinValue)
            .Select(r => r with
            {
                Source = r.Source ?? "",
                Notes = (r.Notes ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
                Internet = (r.Internet ?? []).Where(p => p?.Samples != null).ToList(),
                Router = r.Router?.Samples != null ? r.Router : null,
                LoadedSamples = r.LoadedSamples ?? [],
            })
            .OrderByDescending(r => r.Date)
            .Take(Capacity)
            .ToList();

    private static NetworkPingRun Ping(PingResult p) => new(p.Name, p.Address, p.Samples.ToList());
}
