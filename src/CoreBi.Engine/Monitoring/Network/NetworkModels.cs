using System.Diagnostics;
using System.Net;
using CoreBi.Engine.Monitoring.Health;

namespace CoreBi.Engine.Monitoring.Network;

public enum NetworkPhase { Preparing, Latency, Download, Upload, Analysis, Done }

public enum NetworkSpeedProvider { Cloudflare, Mlab }

public sealed record NetworkProgress(
    NetworkPhase Phase,
    double Fraction,
    string Text,
    double? LatencyMs = null,
    double? DownloadMbps = null,
    double? UploadMbps = null);

public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    string Kind,
    double? LinkMbps,
    string LocalIp,
    IPAddress Gateway,
    string Dns,
    string Mac)
{
    public string LinkText => LinkMbps is not { } mbps ? "—" : mbps >= 1000 ? $"{mbps / 1000:0.#} Gbps" : $"{mbps:0} Mbps";
}

public sealed class PingResult
{
    public PingResult(string name, string address, IReadOnlyList<double?> samples)
    {
        Name = name;
        Address = address;
        Samples = samples;
        var rtts = samples.Where(s => s != null).Select(s => s!.Value).ToList();
        Sent = samples.Count;
        Lost = Sent - rtts.Count;
        if (rtts.Count == 0) return;
        Min = rtts.Min();
        Avg = rtts.Average();
        Max = rtts.Max();
        double jit = 0;
        for (int i = 1; i < rtts.Count; i++) jit += Math.Abs(rtts[i] - rtts[i - 1]);
        Jitter = rtts.Count > 1 ? jit / (rtts.Count - 1) : 0;
    }

    public string Name { get; }
    public string Address { get; }
    public IReadOnlyList<double?> Samples { get; }
    public int Sent { get; }
    public int Lost { get; }
    public double Min { get; }
    public double Avg { get; }
    public double Max { get; }
    public double Jitter { get; }
    public double LossPct => Sent == 0 ? 0 : 100.0 * Lost / Sent;
    public bool Answered => Sent - Lost > 0;
    public string Text => !Answered ? "não responde"
        : $"média {Avg:0.#} ms · mín {Min:0.#} · máx {Max:0.#} · jitter {Jitter:0.#} ms · perda {LossPct:0.#}%";
}

public sealed class NetworkResult
{
    public required HealthCard Card { get; init; }
    public NetworkAdapterInfo? Adapter { get; init; }
    public PingResult? Router { get; init; }
    public IReadOnlyList<PingResult> Internet { get; init; } = [];
    /// <summary>Latência média até a internet (alvos que responderam), jitter do pior alvo e perda média.</summary>
    public double? LatencyMs { get; init; }
    public double? JitterMs { get; init; }
    public double? LossPct { get; init; }
    public double? DnsMs { get; init; }
    public double? DownloadMbps { get; init; }
    public double? UploadMbps { get; init; }
    /// <summary>O upload usa contadores TCP, que incluem a sobrecarga do protocolo.</summary>
    public bool UploadIncludesProtocol { get; init; }
    /// <summary>Pings a 1.1.1.1 enquanto o download enche a conexão.</summary>
    public IReadOnlyList<double?> LoadedSamples { get; init; } = [];
    public double? LoadedLatencyMs { get; init; }
    /// <summary>Quanto a latência sobe com a conexão cheia; acima de <see cref="NetworkThresholds.BufferbloatWarnMs"/> o veredito aponta bufferbloat.</summary>
    public double? BufferbloatMs { get; init; }
    public bool Bufferbloat => BufferbloatMs > NetworkThresholds.BufferbloatWarnMs;
    public HealthVerdict LocalVerdict { get; init; } = HealthVerdict.Unknown;
    public HealthVerdict InternetVerdict { get; init; } = HealthVerdict.Unknown;
    public IReadOnlyList<string> Notes { get; init; } = [];
    public DateTime TestedAt { get; init; } = DateTime.Now;
    /// <summary>Prazo de uma limitação HTTP; enquanto ativo não se inicia outra transferência.</summary>
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public NetworkPhase? InterruptedPhase { get; init; }
    public double InterruptedFraction { get; init; }
}

internal sealed class NetworkTransfer
{
    public long Bytes;
    public volatile string? Error;
    public volatile bool Incomplete;
    public volatile bool IncludesTcpOverhead;
    public readonly Stopwatch Clock = new();
    private long _measurementStartBytes;
    private long _measurementStartTicks;
    private long _lastObservedBytes;

    public bool HasNewBytes()
    {
        long bytes = Interlocked.Read(ref Bytes);
        return bytes > Interlocked.Exchange(ref _lastObservedBytes, bytes);
    }

    public void FinishWarmup()
    {
        long bytes = Interlocked.Read(ref Bytes);
        if (bytes <= 0) return; // Atraso até a primeira resposta continua incluído no resultado.
        Interlocked.Exchange(ref _measurementStartBytes, bytes);
        Interlocked.Exchange(ref _measurementStartTicks, Clock.ElapsedTicks);
    }

    public double? Mbps()
    {
        long bytes = Interlocked.Read(ref Bytes);
        double secs = Clock.Elapsed.TotalSeconds;
        double start = Interlocked.Read(ref _measurementStartTicks) / (double)Stopwatch.Frequency;
        return CalculateMbps(bytes, secs, Interlocked.Read(ref _measurementStartBytes), start);
    }

    internal static double? CalculateMbps(long bytes, double seconds, long warmupBytes, double warmupSeconds)
    {
        if (warmupSeconds > 0 && seconds - warmupSeconds > 0.3 && bytes > warmupBytes)
            return (bytes - warmupBytes) * 8 / 1e6 / (seconds - warmupSeconds);
        return bytes > 0 && seconds > 0.3 ? bytes * 8 / 1e6 / seconds : null;
    }
}
