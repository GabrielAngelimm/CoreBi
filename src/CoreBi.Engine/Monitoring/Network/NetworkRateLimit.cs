using System.Net.Http.Headers;
using System.Text.Json;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Network;

/// <summary>Prazo informado pelo servidor, mantido entre reinícios sem guardar endereços ou outros dados da conexão.</summary>
internal sealed class NetworkRateLimitStore
{
    private sealed record SavedLimit(DateTimeOffset UntilUtc);
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(60);
    private readonly object _sync = new();
    private readonly string _path;
    private DateTimeOffset? _untilUtc;

    public NetworkRateLimitStore(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 1024)
                _untilUtc = JsonSerializer.Deserialize<SavedLimit>(File.ReadAllText(path))?.UntilUtc.ToUniversalTime();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagnosticLog.Write("network.cooldown.load", ex);
        }
    }

    public DateTimeOffset? Active(DateTimeOffset now)
    {
        lock (_sync) return _untilUtc > now ? _untilUtc : null;
    }

    public DateTimeOffset Record(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        DateTimeOffset requested;
        if (retryAfter?.Delta is { } delay && delay > TimeSpan.Zero)
            requested = delay >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now.Add(delay);
        else if (retryAfter?.Date is { } date && date > now)
            requested = date.ToUniversalTime();
        else
            requested = now.Add(DefaultDelay);

        lock (_sync)
        {
            if (_untilUtc is { } existing && existing >= requested) return existing;
            _untilUtc = requested;
            try { AtomicJsonFile.Write(_path, new SavedLimit(requested)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A espera ainda é respeitada nesta sessão mesmo se o Windows impedir a gravação.
                DiagnosticLog.Write("network.cooldown.save", ex);
            }
            return requested;
        }
    }
}

/// <summary>Um único 429 interrompe todas as transferências concorrentes da mesma medição.</summary>
internal sealed class NetworkRateLimitSession(NetworkRateLimitStore store) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private int _limited;
    public CancellationToken Token => _stop.Token;
    public bool Limited => Volatile.Read(ref _limited) != 0;

    public void Stop(RetryConditionHeaderValue? retryAfter)
    {
        if (Interlocked.Exchange(ref _limited, 1) == 0) _stop.Cancel();
        store.Record(retryAfter, DateTimeOffset.UtcNow);
    }

    public void Dispose() => _stop.Dispose();
}
