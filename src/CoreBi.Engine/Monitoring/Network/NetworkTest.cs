using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Health;

namespace CoreBi.Engine.Monitoring.Network;

/// <summary>
/// Diagnóstico de rede: latência, jitter e perda até o roteador e a internet, resolução de DNS,
/// download/upload (Cloudflare ou M-Lab com consentimento) e latência sob carga (bufferbloat).
/// </summary>
public static class NetworkTest
{
    private static readonly Lazy<NetworkRateLimitStore> RateLimit = new(() =>
        new NetworkRateLimitStore(Path.Combine(AppPaths.DataFolder, "network-speed-cooldown.json")));

    private const int PingCount = 30, PingIntervalMs = 200, TickMs = 200, RetryDelayMs = 400;
    private const int InitialUploadBytes = 64_000, MaximumUploadBytes = 25_000_000, DownloadRequestBytes = 50_000_000;
    private const int MaxConsecutiveFailures = 3;
    internal const long DownloadBudgetBytes = 350_000_000, UploadBudgetBytes = 200_000_000;

    private const double LatencyStart = 0.02, DnsStart = 0.31, LatencyEnd = 0.33, DownloadEnd = 0.71, UploadEnd = 0.98;

    private static HttpClient CreateHttpClient()
    {
        // O pool dura uma medição inteira; não usa cookies, compressão ou redirecionamentos de outro domínio.
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return client;
    }

    public static NetworkAdapterInfo? ReadAdapter() => NetworkAdapterProbe.ReadAdapter();

    public static Task<NetworkResult> Run(Action<NetworkProgress> progress, CancellationToken ct,
        NetworkSpeedProvider provider, bool mlabConsent)
    {
        if (!Enum.IsDefined(provider)) throw new ArgumentOutOfRangeException(nameof(provider));
        if (provider == NetworkSpeedProvider.Mlab && !mlabConsent)
            throw new InvalidOperationException("O teste M-Lab exige consentimento para a publicação do IP e dos resultados.");
        return Run(progress, ct, RateLimit.Value, CreateHttpClient, ReadAdapter, provider);
    }

    public static async Task<NetworkResult> RunAutomaticAsync(
        Func<NetworkSpeedProvider, Task<NetworkResult>> run,
        CancellationToken ct,
        Action onFallback)
    {
        var result = await run(NetworkSpeedProvider.Cloudflare);
        ct.ThrowIfCancellationRequested();
        if (result.Adapter == null || result.InterruptedPhase == null) return result;
        onFallback();
        return await run(NetworkSpeedProvider.Mlab);
    }

    internal static async Task<NetworkResult> Run(Action<NetworkProgress> progress, CancellationToken ct,
        NetworkRateLimitStore rateLimit, Func<HttpClient> createClient, Func<NetworkAdapterInfo?> readAdapter,
        NetworkSpeedProvider providerKind = NetworkSpeedProvider.Cloudflare)
    {
        ct.ThrowIfCancellationRequested();
        progress(new(NetworkPhase.Preparing, 0, "Procurando a conexão ativa"));
        var adapter = readAdapter();
        if (adapter == null)
        {
            const string reason = "O Windows não encontrou uma conexão ativa com acesso ao roteador. Verifique o cabo, o Wi-Fi ou o adaptador.";
            return new NetworkResult
            {
                Card = new HealthCard
                {
                    Title = "Sem conexão",
                    Meta = "Nenhum adaptador conectado com gateway",
                    Verdict = HealthVerdict.Critical,
                    Label = "SEM REDE",
                    Reasons = [reason],
                },
                LocalVerdict = HealthVerdict.Critical,
                InternetVerdict = HealthVerdict.Critical,
                Notes = [reason],
            };
        }

        ISpeedTestProvider provider = providerKind == NetworkSpeedProvider.Mlab
            ? new MlabSpeedProvider()
            : new CloudflareSpeedProvider(rateLimit);

        if (provider.GetActiveRateLimit(DateTimeOffset.UtcNow) is { } retryAt)
        {
            string reason = NetworkDiagnostics.RateLimitReason(retryAt);
            progress(new(NetworkPhase.Preparing, 0, "Aguardando o servidor"));
            return new NetworkResult
            {
                Card = new HealthCard
                {
                    Title = "Diagnóstico de rede",
                    Meta = $"{adapter.Kind} · aguardando o servidor",
                    Verdict = HealthVerdict.Warning,
                    Label = "ATENÇÃO",
                    Reasons = [reason],
                },
                Adapter = adapter,
                Notes = [reason],
                RetryAfterUtc = retryAt,
                InterruptedPhase = NetworkPhase.Preparing,
            };
        }

        var loadedTargetIp = IPAddress.Parse("1.1.1.1");
        var targets = new (string Name, IPAddress Addr, List<double?> Samples)[]
        {
            ("Roteador", adapter.Gateway, new()),
            ("Internet · Cloudflare", loadedTargetIp, new()),
            ("Internet · Google", IPAddress.Parse("8.8.8.8"), new()),
        };
        const string pingText = "Medindo latência e perda de pacotes";
        progress(new(NetworkPhase.Latency, LatencyStart, pingText));
        await Track(Task.WhenAll(targets.Select(t => PingLoop(t.Addr, PingCount, PingIntervalMs, t.Samples, ct))), () =>
        {
            var lists = targets.Select(t => Snapshot(t.Samples)).ToList();
            double done = lists.Sum(l => l.Count) / (double)(PingCount * targets.Length);
            var internet = lists.Skip(1).SelectMany(l => l).Where(v => v != null).Select(v => v!.Value).ToList();
            progress(new(NetworkPhase.Latency, LatencyStart + (DnsStart - LatencyStart) * Math.Min(1, done), pingText,
                LatencyMs: internet.Count > 0 ? internet.Average() : null));
        });
        ct.ThrowIfCancellationRequested();

        var pings = targets.Select(t => new PingResult(t.Name, t.Addr.ToString(), Snapshot(t.Samples))).ToArray();
        var routerPing = pings[0];
        var internetPings = pings.Skip(1).ToArray();
        var answeredInternet = internetPings.Where(p => p.Answered).ToList();
        double? netAvg = answeredInternet.Count > 0 ? answeredInternet.Average(p => p.Avg) : null;

        progress(new(NetworkPhase.Latency, DnsStart, "Testando o DNS", LatencyMs: netAvg));
        double? dnsMs = null;
        try
        {
            var sw = Stopwatch.StartNew();
            await Dns.GetHostAddressesAsync("www.cloudflare.com", ct);
            dnsMs = sw.Elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        using var client = createClient();
        using var limit = new NetworkRateLimitSession(rateLimit);
        string? providerError = await provider.PrepareAsync(client, text =>
            progress(new(NetworkPhase.Download, LatencyEnd, text, LatencyMs: netAvg)), ct);

        const string downText = "Medindo a velocidade de download";
        progress(new(NetworkPhase.Download, LatencyEnd, downText, LatencyMs: netAvg));
        using var loadStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loaded = new List<double?>();
        var downTransfer = new NetworkTransfer();
        // Só guarda o ping quando houve dados novos desde a amostra anterior.
        var loadedPing = PingLoop(loadedTargetIp, 0, PingIntervalMs, loaded, loadStop.Token, downTransfer.HasNewBytes);
        double? down = null;
        var downTask = provider.DownloadAsync(downTransfer, client, limit, ct);
        try
        {
            await Track(downTask, () => progress(new(NetworkPhase.Download,
                LatencyEnd + (DownloadEnd - LatencyEnd) * Math.Min(1, downTransfer.Clock.Elapsed.TotalSeconds / provider.DownloadSeconds),
                downText, LatencyMs: netAvg, DownloadMbps: downTransfer.Mbps())));
            down = await downTask;
        }
        catch (OperationCanceledException) { loadStop.Cancel(); throw; }
        catch (Exception ex) { providerError = ex.Message; }
        loadStop.Cancel();
        await loadedPing;

        var loadedSamples = Snapshot(loaded);
        if (down == null) loadedSamples.Clear();
        var loadedRtts = loadedSamples.Where(v => v != null).Select(v => v!.Value).ToList();
        double? loadedMs = NetworkDiagnostics.LoadedLatency(down, loadedRtts);

        const string upText = "Medindo a velocidade de upload";
        var upTransfer = new NetworkTransfer();
        double? up = null;
        bool uploadStarted = provider.CanUpload(limit);
        if (uploadStarted)
        {
            progress(new(NetworkPhase.Upload, DownloadEnd, upText, LatencyMs: netAvg, DownloadMbps: down));
            var upTask = provider.UploadAsync(upTransfer, client, limit, ct);
            try
            {
                await Track(upTask, () => progress(new(NetworkPhase.Upload,
                    DownloadEnd + (UploadEnd - DownloadEnd) * Math.Min(1, upTransfer.Clock.Elapsed.TotalSeconds / provider.UploadSeconds),
                    upText, LatencyMs: netAvg, DownloadMbps: down, UploadMbps: upTransfer.Mbps())));
                up = await upTask;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { providerError ??= ex.Message; }
        }

        if (!limit.Limited)
            progress(new(NetworkPhase.Analysis, UploadEnd, "Analisando",
                LatencyMs: netAvg, DownloadMbps: down, UploadMbps: up));

        DateTimeOffset? limitedUntil = limit.Limited ? rateLimit.Active(DateTimeOffset.UtcNow) ?? DateTimeOffset.UtcNow.AddSeconds(60) : null;
        NetworkPhase? interrupted = limitedUntil != null ? (uploadStarted ? NetworkPhase.Upload : NetworkPhase.Download)
            : down == null ? NetworkPhase.Download : up == null ? NetworkPhase.Upload : null;
        double interruptedFraction = interrupted switch
        {
            NetworkPhase.Download => LatencyEnd + (DownloadEnd - LatencyEnd) * Math.Min(1, downTransfer.Clock.Elapsed.TotalSeconds / provider.DownloadSeconds),
            NetworkPhase.Upload => DownloadEnd + (UploadEnd - DownloadEnd) * Math.Min(1, upTransfer.Clock.Elapsed.TotalSeconds / provider.UploadSeconds),
            _ => 0,
        };

        var diag = NetworkDiagnostics.Analyze(
            routerPing,
            internetPings,
            internetPings[0],
            dnsMs,
            down,
            up,
            downTransfer.Error,
            upTransfer.Error,
            upTransfer.Incomplete,
            providerError,
            loadedMs,
            limitedUntil,
            limit.Limited);

        var metrics = new List<KeyValuePair<string, string>>
        {
            new("Servidor de velocidade", provider.DisplayName),
        };
        foreach (var p in pings) metrics.Add(new($"{p.Name} ({p.Address})", p.Text));
        if (dnsMs != null) metrics.Add(new("Resolução de nome (pode usar cache)", $"{dnsMs:0} ms"));
        metrics.Add(new("Download", down != null ? $"{down:0.#} Mbps" : "—"));
        metrics.Add(new("Upload", up != null ? $"{up:0.#} Mbps" : "—"));
        if (up != null && upTransfer.IncludesTcpOverhead) metrics.Add(new("Medição do upload", "contador TCP do servidor (inclui protocolo)"));
        if (loadedMs != null) metrics.Add(new("Latência sob carga", $"{loadedMs:0} ms (durante o download)"));
        metrics.Add(new("Adaptador", $"{adapter.Name} · {adapter.Kind} · link {adapter.LinkText}"));
        metrics.Add(new("IP / Gateway / DNS", $"{adapter.LocalIp} / {adapter.Gateway} / {adapter.Dns}"));

        var now = DateTime.Now;
        progress(new(interrupted ?? NetworkPhase.Done, interrupted != null ? interruptedFraction : 1,
            limitedUntil != null ? "Aguardando o servidor" : interrupted != null ? "Teste incompleto" : "Concluído",
            LatencyMs: diag.LatencyMs, DownloadMbps: down, UploadMbps: up));

        return new NetworkResult
        {
            Card = new HealthCard
            {
                Title = "Diagnóstico de rede",
                Meta = $"{adapter.Kind} · {provider.DisplayName} · testado às {now:HH:mm:ss}",
                Verdict = diag.Overall,
                Label = diag.Label,
                Reasons = diag.Notes.ToList(),
                Metrics = metrics,
            },
            Adapter = adapter,
            Router = routerPing,
            Internet = internetPings,
            LatencyMs = diag.LatencyMs,
            JitterMs = diag.JitterMs,
            LossPct = diag.LossPct,
            DnsMs = dnsMs,
            DownloadMbps = down,
            UploadMbps = up,
            UploadIncludesProtocol = upTransfer.IncludesTcpOverhead,
            LoadedSamples = loadedSamples,
            LoadedLatencyMs = loadedMs,
            BufferbloatMs = diag.BufferbloatMs,
            LocalVerdict = diag.LocalVerdict,
            InternetVerdict = diag.InternetVerdict,
            Notes = diag.Notes,
            TestedAt = now,
            RetryAfterUtc = limitedUntil,
            InterruptedPhase = interrupted,
            InterruptedFraction = interruptedFraction,
        };
    }

    internal static async Task<double?> Download(NetworkTransfer transfer, HttpClient client, NetworkRateLimitSession limit,
        TimeSpan duration, TimeSpan warmup, int streams, CancellationToken ct, long byteBudget = DownloadBudgetBytes)
    {
        ct.ThrowIfCancellationRequested();
        if (byteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(byteBudget));
        transfer.Clock.Restart();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        cts.CancelAfter(duration);
        var tasks = Enumerable.Range(0, streams).Select(_ => Task.Run(async () =>
        {
            var buf = new byte[81920];
            int failures = 0;
            while (!cts.IsCancellationRequested && !limit.Limited)
            {
                try
                {
                    using var resp = await client.GetAsync($"https://speed.cloudflare.com/__down?bytes={DownloadRequestBytes}", HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        limit.Stop(resp.Headers.RetryAfter);
                        break;
                    }
                    resp.EnsureSuccessStatusCode();
                    using var st = await resp.Content.ReadAsStreamAsync(cts.Token);
                    long requestBytes = 0;
                    int n;
                    while ((n = await st.ReadAsync(buf, cts.Token)) > 0)
                    {
                        requestBytes += n;
                        if (Interlocked.Add(ref transfer.Bytes, n) >= byteBudget)
                        {
                            cts.Cancel();
                            break;
                        }
                    }
                    if (!cts.IsCancellationRequested && requestBytes != DownloadRequestBytes)
                        throw new IOException("Resposta de download incompleta.");
                    failures = 0;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    transfer.Error = TransferError(ex);
                    if (++failures >= MaxConsecutiveFailures || PermanentFailure(ex)) { transfer.Incomplete = true; break; }
                    try { await Task.Delay(RetryDelayMs * failures, cts.Token); } catch (OperationCanceledException) { }
                }
            }
        })).ToArray();
        try
        {
            await Task.Delay(warmup, cts.Token);
            transfer.FinishWarmup();
        }
        catch (OperationCanceledException) { }
        await Task.WhenAll(tasks);
        transfer.Clock.Stop();
        ct.ThrowIfCancellationRequested();
        return limit.Limited ? null : transfer.Mbps();
    }

    internal static async Task<double?> Upload(NetworkTransfer transfer, HttpClient client, NetworkRateLimitSession limit,
        TimeSpan duration, TimeSpan grace, int streams, CancellationToken ct, long byteBudget = UploadBudgetBytes)
    {
        ct.ThrowIfCancellationRequested();
        if (byteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(byteBudget));
        var payload = new byte[MaximumUploadBytes];
        new Random(1).NextBytes(payload);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        cts.CancelAfter(duration + grace);
        transfer.Clock.Restart();
        long reserved = 0;
        var tasks = Enumerable.Range(0, streams).Select(_ => Task.Run(async () =>
        {
            int size = InitialUploadBytes;
            int failures = 0;
            while (!cts.IsCancellationRequested && !limit.Limited && transfer.Clock.Elapsed < duration)
            {
                try
                {
                    int chunkSize = ReserveUploadBytes(ref reserved, size, byteBudget);
                    if (chunkSize == 0) break;
                    using var content = new ByteArrayContent(payload, 0, chunkSize);
                    var requestClock = Stopwatch.StartNew();
                    using var resp = await client.PostAsync("https://speed.cloudflare.com/__up", content, cts.Token);
                    if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        limit.Stop(resp.Headers.RetryAfter);
                        break;
                    }
                    resp.EnsureSuccessStatusCode();
                    Interlocked.Add(ref transfer.Bytes, chunkSize);
                    failures = 0;
                    // Lotes maiores amortizam a ida e volta HTTP em conexões rápidas; os pequenos preservam as lentas.
                    size = (int)Math.Clamp(chunkSize * 1.2 / Math.Max(0.001, requestClock.Elapsed.TotalSeconds),
                        InitialUploadBytes, MaximumUploadBytes);
                }
                catch (OperationCanceledException) { if (!ct.IsCancellationRequested && !limit.Limited) transfer.Incomplete = true; }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    transfer.Error = TransferError(ex);
                    if (++failures >= MaxConsecutiveFailures || PermanentFailure(ex)) { transfer.Incomplete = true; break; }
                    if (transfer.Clock.Elapsed < duration)
                        try { await Task.Delay(RetryDelayMs * failures, cts.Token); } catch (OperationCanceledException) { }
                }
            }
        })).ToArray();
        await Task.WhenAll(tasks);
        transfer.Clock.Stop();
        ct.ThrowIfCancellationRequested();
        return limit.Limited ? null : transfer.Mbps();
    }

    private static async Task PingLoop(IPAddress ip, int count, int intervalMs, List<double?> into, CancellationToken ct,
        Func<bool>? includeSample = null)
    {
        using var p = new Ping();
        for (int i = 0; (count <= 0 || i < count) && !ct.IsCancellationRequested; i++)
        {
            var sw = Stopwatch.StartNew();
            double? rtt = null;
            try
            {
                var r = await p.SendPingAsync(ip, 1500);
                if (r.Status == IPStatus.Success) rtt = Math.Max(1, r.RoundtripTime);
            }
            catch (PingException) { }
            catch (InvalidOperationException) { }
            if (includeSample == null || includeSample())
                lock (into) into.Add(rtt);
            int wait = intervalMs - (int)sw.ElapsedMilliseconds;
            if (wait > 0) { try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; } }
        }
    }

    private static List<double?> Snapshot(List<double?> samples) { lock (samples) return samples.ToList(); }

    private static async Task Track(Task work, Action tick)
    {
        while (await Task.WhenAny(work, Task.Delay(TickMs)) != work) tick();
        await work;
    }

    private static bool PermanentFailure(Exception ex) => ex is HttpRequestException { StatusCode: { } status }
        && (int)status is >= 300 and < 500 && status != HttpStatusCode.RequestTimeout;

    private static string TransferError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "servidor recusou a solicitação",
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout } => "tempo de espera esgotado",
        HttpRequestException { StatusCode: { } status } => $"HTTP {(int)status}",
        HttpRequestException => "falha de conexão com o servidor",
        IOException => "falha durante a transferência",
        _ => "falha na medição",
    };

    private static int ReserveUploadBytes(ref long reserved, int requested, long budget)
    {
        while (true)
        {
            long current = Interlocked.Read(ref reserved);
            if (current >= budget) return 0;
            int granted = (int)Math.Min(requested, budget - current);
            if (Interlocked.CompareExchange(ref reserved, current + granted, current) == current) return granted;
        }
    }
}
