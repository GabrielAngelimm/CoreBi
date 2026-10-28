using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Persistence;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CoreBi.Engine.Monitoring.Network;

/// <summary>Etapa do teste de rede, na ordem em que acontecem.</summary>
public enum NetworkPhase { Preparing, Latency, Download, Upload, Analysis, Done }
public enum NetworkSpeedProvider { Cloudflare, Mlab }

/// <summary>
/// Progresso do teste: a fração vem do trabalho real (pings enviados, segundos medidos de cada fase) e as leituras
/// parciais são o que já foi medido até aqui (latência média, Mbps acumulados).
/// </summary>
public sealed record NetworkProgress(NetworkPhase Phase, double Fraction, string Text,
    double? LatencyMs = null, double? DownloadMbps = null, double? UploadMbps = null);

/// <summary>Adaptador com rota para o roteador, como o Windows informa.</summary>
public sealed record NetworkAdapterInfo(string Name, string Description, string Kind, double? LinkMbps,
    string LocalIp, IPAddress Gateway, string Dns, string Mac)
{
    public string LinkText => LinkMbps is not { } mbps ? "—" : mbps >= 1000 ? $"{mbps / 1000:0.#} Gbps" : $"{mbps:0} Mbps";
}

/// <summary>Uma série de pings: cada amostra é o tempo de ida e volta em ms, ou null quando o pacote se perdeu.</summary>
public sealed class PingResult
{
    public PingResult(string name, string address, IReadOnlyList<double?> samples)
    {
        Name = name; Address = address; Samples = samples;
        var rtts = samples.Where(s => s != null).Select(s => s!.Value).ToList();
        Sent = samples.Count; Lost = Sent - rtts.Count;
        if (rtts.Count == 0) return;
        Min = rtts.Min(); Avg = rtts.Average(); Max = rtts.Max();
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

/// <summary>Resultado completo: o cartão de veredito (usado no relatório) e as medidas que a página desenha.</summary>
public sealed class NetworkResult
{
    public required DiskHealthCard Card { get; init; }
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
    /// <summary>Quanto a latência sobe com a conexão cheia; acima de 100 ms o veredito aponta bufferbloat.</summary>
    public double? BufferbloatMs { get; init; }
    public bool Bufferbloat => BufferbloatMs > 100;
    /// <summary>Veredito de cada trecho: do PC ao roteador e do roteador à internet (Unknown quando o alvo não responde a ping).</summary>
    public DiskVerdict LocalVerdict { get; init; } = DiskVerdict.Unknown;
    public DiskVerdict InternetVerdict { get; init; } = DiskVerdict.Unknown;
    /// <summary>Uma frase por achado; quando tudo está bem, a frase de conexão estável.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
    public DateTime TestedAt { get; init; } = DateTime.Now;
    /// <summary>Prazo de uma limitação HTTP; enquanto ativo não se inicia outra transferência.</summary>
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public NetworkPhase? InterruptedPhase { get; init; }
    public double InterruptedFraction { get; init; }
}

/// <summary>
/// Diagnóstico de rede para lag: latência, jitter e perda de pacotes até o roteador e até a internet,
/// resolução de DNS, velocidade de download/upload e latência sob carga (bufferbloat).
/// Os testes de velocidade usam a Cloudflare ou, com consentimento, o M-Lab.
/// </summary>
public static class NetworkTest
{
    private static readonly NetworkRateLimitStore RateLimit = new(Path.Combine(AppSettings.Folder, "network-speed-cooldown.json"));

    private const int PingCount = 30, PingIntervalMs = 200, DownloadSeconds = 8, UploadSeconds = 6, TickMs = 200, RetryDelayMs = 400;
    private const int DownloadStreams = 4, UploadStreams = 3, DownloadWarmupMs = 1000, UploadGraceMs = 3000;
    private const int InitialUploadBytes = 64_000, MaximumUploadBytes = 25_000_000, DownloadRequestBytes = 50_000_000;
    private const int MaxConsecutiveFailures = 3;
    private const long DownloadBudgetBytes = 350_000_000, UploadBudgetBytes = 200_000_000;

    // Faixas da barra de progresso, proporcionais ao tempo de cada fase (≈ 6 s, 8 s e 6 s).
    public const double LatencyEnd = 0.33, DownloadEnd = 0.71;
    private const double LatencyStart = 0.02, DnsStart = 0.31, UploadEnd = 0.98;

    private static HttpClient CreateHttpClient()
    {
        // O pool dura uma medição inteira; não usa cookies, compressão ou redirecionamentos de outro domínio.
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return client;
    }

    /// <summary>Bytes transferidos e relógio de uma fase de velocidade, lidos pelo progresso enquanto ela corre.</summary>
    internal sealed class Transfer
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
    }

    internal static double? CalculateMbps(long bytes, double seconds, long warmupBytes, double warmupSeconds)
    {
        if (warmupSeconds > 0 && seconds - warmupSeconds > 0.3 && bytes > warmupBytes)
            return (bytes - warmupBytes) * 8 / 1e6 / (seconds - warmupSeconds);
        return bytes > 0 && seconds > 0.3 ? bytes * 8 / 1e6 / seconds : null;
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

    /// <summary>Repete <paramref name="tick"/> a cada 200 ms até o trabalho terminar.</summary>
    private static async Task Track(Task work, Action tick)
    {
        while (await Task.WhenAny(work, Task.Delay(TickMs)) != work) tick();
        await work;
    }

    internal static async Task<double?> Download(Transfer transfer, HttpClient client, NetworkRateLimitSession limit,
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

    internal static async Task<double?> Upload(Transfer transfer, HttpClient client, NetworkRateLimitSession limit,
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

    private static string AdapterType(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet (cabo)",
        _ => t.ToString()
    };

    /// <summary>Prioriza o adaptador da rota IPv4 para a internet; usa o maior link com gateway como alternativa.</summary>
    public static NetworkAdapterInfo? ReadAdapter()
    {
        try
        {
            var localRoute = RoutedLocalAddress();
            var selected = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => new { Nic = n, Properties = n.GetIPProperties() })
                .Where(n => n.Properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                   && !g.Address.Equals(IPAddress.Any)))
                .OrderByDescending(n => localRoute != null && n.Properties.UnicastAddresses.Any(u => u.Address.Equals(localRoute)))
                .ThenByDescending(n => n.Nic.Speed).FirstOrDefault();
            if (selected == null) return null;
            var nic = selected.Nic;
            var props = selected.Properties;
            var gw = props.GatewayAddresses.First(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Address;
            string ip = localRoute != null && props.UnicastAddresses.Any(u => u.Address.Equals(localRoute))
                ? localRoute.ToString()
                : props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "—";
            string dns = props.DnsAddresses.FirstOrDefault(d => d.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "—";
            var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
            string mac = macBytes.Length > 0 ? string.Join("-", macBytes.Select(b => b.ToString("X2"))) : "—";
            return new NetworkAdapterInfo(nic.Name, nic.Description, AdapterType(nic.NetworkInterfaceType),
                nic.Speed > 0 ? nic.Speed / 1_000_000.0 : null, ip, gw, dns, mac);
        }
        catch { return null; }
    }

    private static IPAddress? RoutedLocalAddress()
    {
        try
        {
            // Connect em UDP consulta a rota do Windows; não envia um pacote ao destino.
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException) { return null; }
    }

    public static Task<NetworkResult> Run(Action<NetworkProgress> progress, CancellationToken ct) =>
        Run(progress, ct, RateLimit, CreateHttpClient, ReadAdapter);

    public static Task<NetworkResult> Run(Action<NetworkProgress> progress, CancellationToken ct,
        NetworkSpeedProvider provider, bool mlabConsent)
    {
        if (!Enum.IsDefined(provider)) throw new ArgumentOutOfRangeException(nameof(provider));
        if (provider == NetworkSpeedProvider.Mlab && !mlabConsent)
            throw new InvalidOperationException("O teste M-Lab exige consentimento para a publicação do IP e dos resultados.");
        return Run(progress, ct, RateLimit, CreateHttpClient, ReadAdapter, provider);
    }

    internal static async Task<NetworkResult> Run(Action<NetworkProgress> progress, CancellationToken ct,
        NetworkRateLimitStore rateLimit, Func<HttpClient> createClient, Func<NetworkAdapterInfo?> readAdapter,
        NetworkSpeedProvider provider = NetworkSpeedProvider.Cloudflare)
    {
        ct.ThrowIfCancellationRequested();
        progress(new(NetworkPhase.Preparing, 0, "Procurando a conexão ativa"));
        var adapter = readAdapter();
        if (adapter == null)
        {
            const string reason = "O Windows não encontrou uma conexão ativa com acesso ao roteador. Verifique o cabo, o Wi-Fi ou o adaptador.";
            return new NetworkResult
            {
                Card = new DiskHealthCard
                {
                    Title = "Sem conexão",
                    Meta = "Nenhum adaptador conectado com gateway",
                    Verdict = DiskVerdict.Critical,
                    Label = "SEM REDE",
                    Reasons = reason,
                },
                LocalVerdict = DiskVerdict.Critical,
                InternetVerdict = DiskVerdict.Critical,
                Notes = [reason],
            };
        }

        if (ActiveSpeedLimit(provider, rateLimit, DateTimeOffset.UtcNow) is { } retryAt)
        {
            string reason = RateLimitReason(retryAt);
            progress(new(NetworkPhase.Preparing, 0, "Aguardando o servidor"));
            return new NetworkResult
            {
                Card = new DiskHealthCard
                {
                    Title = "Diagnóstico de rede",
                    Meta = $"{adapter.Kind} · aguardando o servidor",
                    Verdict = DiskVerdict.Warning,
                    Label = "ATENÇÃO",
                    Reasons = reason,
                },
                Adapter = adapter,
                Notes = [reason],
                RetryAfterUtc = retryAt,
                InterruptedPhase = NetworkPhase.Preparing,
            };
        }

        // 1) Latência / jitter / perda: roteador e dois DNS públicos, em paralelo (30 pacotes cada)
        var targets = new (string Name, IPAddress Addr, List<double?> Samples)[]
        {
            ("Roteador", adapter.Gateway, new()), ("Internet · Cloudflare", IPAddress.Parse("1.1.1.1"), new()), ("Internet · Google", IPAddress.Parse("8.8.8.8"), new()),
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
        var gwS = pings[0];
        var net = pings.Skip(1).Where(p => p.Answered).ToList();
        double? netAvg = net.Count > 0 ? net.Average(p => p.Avg) : null;

        // 2) DNS
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

        // 3) Download com ping em paralelo (latência sob carga)
        using var client = createClient();
        using var limit = new NetworkRateLimitSession(rateLimit);
        string providerLabel = provider == NetworkSpeedProvider.Mlab ? "M-Lab" : "Cloudflare";
        string? netErr = null;
        MlabSpeedTest.Target? mlab = null;
        if (provider == NetworkSpeedProvider.Mlab)
        {
            progress(new(NetworkPhase.Download, LatencyEnd, "Procurando servidor M-Lab", LatencyMs: netAvg));
            try { mlab = await MlabSpeedTest.Locate(client, ct); providerLabel = mlab.Location; }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException ex) { netErr = ex.Message; }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            { netErr = "Não foi possível encontrar um servidor M-Lab disponível."; }
        }
        const string downText = "Medindo a velocidade de download";
        progress(new(NetworkPhase.Download, LatencyEnd, downText, LatencyMs: netAvg));
        using var loadStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loaded = new List<double?>();
        var downT = new Transfer();
        // Só guarda o ping quando houve dados novos desde a amostra anterior.
        var loadedPing = PingLoop(IPAddress.Parse("1.1.1.1"), 0, PingIntervalMs, loaded, loadStop.Token, downT.HasNewBytes);
        double? down = null;
        var downTask = provider == NetworkSpeedProvider.Cloudflare
            ? Download(downT, client, limit, TimeSpan.FromSeconds(DownloadSeconds),
                TimeSpan.FromMilliseconds(DownloadWarmupMs), DownloadStreams, ct)
            : mlab != null ? MlabSpeedTest.Download(downT, mlab.Download, ct) : Task.FromResult<double?>(null);
        int downSeconds = provider == NetworkSpeedProvider.Mlab ? 10 : DownloadSeconds;
        try
        {
            await Track(downTask, () => progress(new(NetworkPhase.Download,
                LatencyEnd + (DownloadEnd - LatencyEnd) * Math.Min(1, downT.Clock.Elapsed.TotalSeconds / downSeconds),
                downText, LatencyMs: netAvg, DownloadMbps: downT.Mbps())));
            down = await downTask;
        }
        catch (OperationCanceledException) { loadStop.Cancel(); throw; }
        catch (Exception ex) { netErr = ex.Message; }
        loadStop.Cancel();
        await loadedPing;
        var loadedSamples = Snapshot(loaded);
        if (down == null) loadedSamples.Clear();
        var loadedRtts = loadedSamples.Where(v => v != null).Select(v => v!.Value).ToList();
        double? loadedMs = LoadedLatency(down, loadedRtts);

        // 4) Upload
        const string upText = "Medindo a velocidade de upload";
        var upT = new Transfer();
        double? up = null;
        bool uploadStarted = !limit.Limited && (provider == NetworkSpeedProvider.Cloudflare || mlab != null);
        if (uploadStarted)
        {
            progress(new(NetworkPhase.Upload, DownloadEnd, upText, LatencyMs: netAvg, DownloadMbps: down));
            var upTask = provider == NetworkSpeedProvider.Cloudflare
                ? Upload(upT, client, limit, TimeSpan.FromSeconds(UploadSeconds),
                    TimeSpan.FromMilliseconds(UploadGraceMs), UploadStreams, ct)
                : MlabSpeedTest.Upload(upT, mlab!.Upload, ct);
            int upSeconds = provider == NetworkSpeedProvider.Mlab ? 10 : UploadSeconds;
            try
            {
                await Track(upTask, () => progress(new(NetworkPhase.Upload,
                    DownloadEnd + (UploadEnd - DownloadEnd) * Math.Min(1, upT.Clock.Elapsed.TotalSeconds / upSeconds),
                    upText, LatencyMs: netAvg, DownloadMbps: down, UploadMbps: upT.Mbps())));
                up = await upTask;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { netErr ??= ex.Message; }
        }

        if (!limit.Limited)
            progress(new(NetworkPhase.Analysis, UploadEnd, "Analisando",
                LatencyMs: netAvg, DownloadMbps: down, UploadMbps: up));

        DateTimeOffset? limitedUntil = limit.Limited ? rateLimit.Active(DateTimeOffset.UtcNow) ?? DateTimeOffset.UtcNow.AddSeconds(60) : null;
        NetworkPhase? interrupted = limitedUntil != null ? (uploadStarted ? NetworkPhase.Upload : NetworkPhase.Download)
            : down == null ? NetworkPhase.Download : up == null ? NetworkPhase.Upload : null;
        double interruptedFraction = interrupted switch
        {
            NetworkPhase.Download => LatencyEnd + (DownloadEnd - LatencyEnd) * Math.Min(1, downT.Clock.Elapsed.TotalSeconds / DownloadSeconds),
            NetworkPhase.Upload => DownloadEnd + (UploadEnd - DownloadEnd) * Math.Min(1, upT.Clock.Elapsed.TotalSeconds / UploadSeconds),
            _ => 0,
        };

        // ---- Veredito ----
        double? netLoss = InternetLoss(pings.Skip(1));
        double netJit = net.Count > 0 ? net.Max(p => p.Jitter) : 0;
        var warn = new List<string>();
        if (limitedUntil is { } until) warn.Add(RateLimitReason(until));
        bool bad = false;

        bool lanBad = gwS.Answered && (gwS.LossPct > 2 || gwS.Avg > 30 || gwS.Jitter > 15);
        if (lanBad) warn.Add($"A rede local está instável (roteador: {gwS.Avg:0} ms, perda {gwS.LossPct:0.#}%, jitter {gwS.Jitter:0} ms). Se for Wi-Fi, aproxime-se do roteador ou use cabo.");
        if (!gwS.Answered) warn.Add("O roteador não responde a ping (alguns modelos bloqueiam). Isso por si só não é problema.");
        var internetVerdict = DiskVerdict.Good;
        if (net.Count == 0)
        {
            bad = !HasHttpEvidence(down, up) && !limit.Limited;
            internetVerdict = bad ? DiskVerdict.Critical : DiskVerdict.Unknown;
            warn.Add(bad ? "Sem resposta ICMP ou HTTP dos alvos testados; verifique a conexão."
                : "HTTP funcionou, mas os alvos não responderam a ICMP. A latência e a perda até esses alvos são inconclusivas.");
        }
        else
        {
            if (net.Count < pings.Length - 1)
            {
                warn.Add("Um dos alvos de internet não respondeu a ping; a perda nesse alvo é inconclusiva.");
                internetVerdict = DiskVerdict.Warning;
            }
            if (netLoss > 5 || netAvg > 150) { bad = true; internetVerdict = DiskVerdict.Critical; }
            else if (netLoss > 1 || netAvg > 80 || netJit > 20) internetVerdict = DiskVerdict.Warning;
            if (netLoss > 1) warn.Add($"Há perda de pacotes nos alvos que responderam ({netLoss:0.#}%). Verifique a rede local e, se persistir, o provedor.");
            if (netAvg > 80) warn.Add($"Latência alta até os alvos de internet ({netAvg:0} ms). O teste não identifica sozinho a origem do atraso.");
            if (netJit > 20) warn.Add($"O jitter está alto ({netJit:0} ms): a latência oscila, o que causa engasgos em jogos online.");
        }
        double? bloat = Bufferbloat(pings[1], loadedMs);
        if (bloat > 100)
            warn.Add($"A latência sobe para {loadedMs:0} ms quando a conexão está cheia (bufferbloat). Ative SQM/QoS no roteador ou limite a velocidade a ~90%.");
        if (dnsMs > 150) warn.Add($"A resolução do nome demorou {dnsMs:0} ms. O resultado pode incluir cache do Windows e atrasos da rede.");
        if (down is < 10) warn.Add($"Download baixo ({down:0.#} Mbps) para jogos, streaming e atualizações.");
        if (limitedUntil == null)
        {
            if (down == null && up == null) warn.Add(netErr ?? $"Não foi possível medir a velocidade{Reason(downT.Error ?? upT.Error)}.");
            else if (down == null) warn.Add($"Não foi possível medir o download{Reason(downT.Error ?? netErr)}.");
            else if (up == null) warn.Add($"Não foi possível medir o upload{Reason(upT.Error ?? netErr)}.");
        }
        if (down != null && downT.Error != null) warn.Add("Houve falhas durante o download; o resultado pode estar impreciso.");
        if (up != null && (upT.Incomplete || upT.Error != null)) warn.Add("Houve falhas ou envios incompletos no upload; o resultado pode estar impreciso.");

        var verdict = bad ? DiskVerdict.Critical : warn.Any(w => !w.StartsWith("O roteador não responde")) ? DiskVerdict.Warning : DiskVerdict.Good;
        string label = verdict switch { DiskVerdict.Critical => "RUIM", DiskVerdict.Warning => "ATENÇÃO", _ => "ESTÁVEL" };
        string stable = bloat != null
            ? "Latência baixa, sem perda nos alvos testados e sem sinais de bufferbloat durante o download."
            : "Latência baixa e sem perda nos alvos testados; a comparação da latência sob carga ficou inconclusiva.";
        string reasons = warn.Count > 0 ? string.Join(" ", warn) : stable;

        var m = new List<KeyValuePair<string, string>>();
        m.Add(new("Servidor de velocidade", providerLabel));
        foreach (var p in pings) m.Add(new($"{p.Name} ({p.Address})", p.Text));
        if (dnsMs != null) m.Add(new("Resolução de nome (pode usar cache)", $"{dnsMs:0} ms"));
        m.Add(new("Download", down != null ? $"{down:0.#} Mbps" : "—"));
        m.Add(new("Upload", up != null ? $"{up:0.#} Mbps" : "—"));
        if (up != null && upT.IncludesTcpOverhead) m.Add(new("Medição do upload", "contador TCP do servidor (inclui protocolo)"));
        if (loadedMs != null) m.Add(new("Latência sob carga", $"{loadedMs:0} ms (durante o download)"));
        m.Add(new("Adaptador", $"{adapter.Name} · {adapter.Kind} · link {adapter.LinkText}"));
        m.Add(new("IP / Gateway / DNS", $"{adapter.LocalIp} / {adapter.Gateway} / {adapter.Dns}"));

        var now = DateTime.Now;
        progress(new(interrupted ?? NetworkPhase.Done, interrupted != null ? interruptedFraction : 1,
            limitedUntil != null ? "Aguardando o servidor" : interrupted != null ? "Teste incompleto" : "Concluído",
            LatencyMs: netAvg, DownloadMbps: down, UploadMbps: up));
        return new NetworkResult
        {
            Card = new DiskHealthCard
            {
                Title = "Diagnóstico de rede",
                Meta = $"{adapter.Kind} · {providerLabel} · testado às {now:HH:mm:ss}",
                Verdict = verdict,
                Label = label,
                Reasons = reasons,
                Metrics = m,
            },
            Adapter = adapter,
            Router = gwS,
            Internet = pings.Skip(1).ToList(),
            LatencyMs = netAvg,
            JitterMs = net.Count > 0 ? netJit : null,
            LossPct = netLoss,
            DnsMs = dnsMs,
            DownloadMbps = down,
            UploadMbps = up,
            UploadIncludesProtocol = upT.IncludesTcpOverhead,
            LoadedSamples = loadedSamples,
            LoadedLatencyMs = loadedMs,
            BufferbloatMs = bloat,
            LocalVerdict = !gwS.Answered ? DiskVerdict.Unknown : lanBad ? DiskVerdict.Warning : DiskVerdict.Good,
            InternetVerdict = internetVerdict,
            Notes = warn.Count > 0 ? warn : [stable],
            TestedAt = now,
            RetryAfterUtc = limitedUntil,
            InterruptedPhase = interrupted,
            InterruptedFraction = interruptedFraction,
        };
    }

    private static string Reason(string? error) => string.IsNullOrEmpty(error) ? "" : $" ({error})";

    private static string RateLimitReason(DateTimeOffset until) =>
        $"O servidor da Cloudflare pediu uma pausa por excesso de requisições. Tente novamente após {until.LocalDateTime:dd/MM 'às' HH:mm:ss}.";

    internal static DateTimeOffset? ActiveSpeedLimit(NetworkSpeedProvider provider, NetworkRateLimitStore store, DateTimeOffset now) =>
        provider == NetworkSpeedProvider.Cloudflare ? store.Active(now) : null;

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
}
