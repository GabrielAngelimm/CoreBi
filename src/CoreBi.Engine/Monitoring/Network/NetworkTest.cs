using CoreBi.Engine.Monitoring.Storage;

using System.Diagnostics;
using System.Net;

using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CoreBi.Engine.Monitoring.Network;

/// <summary>Etapa do teste de rede, na ordem em que acontecem.</summary>
public enum NetworkPhase { Preparing, Latency, Download, Upload, Analysis, Done }

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
}

/// <summary>
/// Diagnóstico de rede para lag: latência, jitter e perda de pacotes até o roteador e até a internet,
/// resolução de DNS, velocidade de download/upload e latência sob carga (bufferbloat).
/// Os testes de velocidade usam o servidor público speed.cloudflare.com. Não grava nada no disco.
/// </summary>
public static class NetworkTest
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const int PingCount = 30, PingIntervalMs = 200, DownloadSeconds = 8, UploadSeconds = 6, TickMs = 200, RetryDelayMs = 400;
    private const int DownloadStreams = 4, UploadStreams = 3;

    // Faixas da barra de progresso, proporcionais ao tempo de cada fase (≈ 6 s, 8 s e 6 s).
    public const double LatencyEnd = 0.33, DownloadEnd = 0.71;
    private const double LatencyStart = 0.02, DnsStart = 0.31, UploadEnd = 0.98;

    /// <summary>Bytes transferidos e relógio de uma fase de velocidade, lidos pelo progresso enquanto ela corre.</summary>
    private sealed class Transfer
    {
        public long Bytes;
        public volatile string? Error;
        public readonly Stopwatch Clock = new();
        public double? Mbps()
        {
            double secs = Clock.Elapsed.TotalSeconds;
            long bytes = Interlocked.Read(ref Bytes);
            return bytes > 0 && secs > 0.3 ? bytes * 8 / 1e6 / secs : null;
        }
    }

    private static async Task PingLoop(IPAddress ip, int count, int intervalMs, List<double?> into, CancellationToken ct)
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

    private static async Task<double?> Download(Transfer transfer, CancellationToken ct)
    {
        transfer.Clock.Restart();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DownloadSeconds * 1000);
        var tasks = Enumerable.Range(0, DownloadStreams).Select(_ => Task.Run(async () =>
        {
            var buf = new byte[81920];
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var resp = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    resp.EnsureSuccessStatusCode();
                    using var st = await resp.Content.ReadAsStreamAsync(cts.Token);
                    int n;
                    while ((n = await st.ReadAsync(buf, cts.Token)) > 0) Interlocked.Add(ref transfer.Bytes, n);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    // O servidor pode recusar conexões em sequência (limite de requisições): espera um pouco e tenta de novo.
                    transfer.Error = (ex as HttpRequestException)?.StatusCode?.ToString() ?? ex.Message;
                    try { await Task.Delay(RetryDelayMs, cts.Token); } catch (OperationCanceledException) { }
                }
            }
        })).ToArray();
        await Task.WhenAll(tasks);
        transfer.Clock.Stop();
        ct.ThrowIfCancellationRequested();
        return transfer.Mbps();
    }

    private static async Task<double?> Upload(Transfer transfer, CancellationToken ct)
    {
        transfer.Clock.Restart();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(UploadSeconds * 1000);
        var payload = new byte[1_000_000];
        new Random(1).NextBytes(payload);
        var tasks = Enumerable.Range(0, UploadStreams).Select(_ => Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var resp = await Http.PostAsync("https://speed.cloudflare.com/__up", new ByteArrayContent(payload), cts.Token);
                    resp.EnsureSuccessStatusCode();
                    Interlocked.Add(ref transfer.Bytes, payload.Length);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    transfer.Error = (ex as HttpRequestException)?.StatusCode?.ToString() ?? ex.Message;
                    try { await Task.Delay(RetryDelayMs, cts.Token); } catch (OperationCanceledException) { }
                }
            }
        })).ToArray();
        await Task.WhenAll(tasks);
        transfer.Clock.Stop();
        ct.ThrowIfCancellationRequested();
        return transfer.Mbps();
    }

    private static string AdapterType(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet (cabo)",
        _ => t.ToString()
    };

    /// <summary>Adaptador conectado com gateway IPv4 (o mais rápido, se houver vários), ou null sem conexão. Leitura rápida, sem tráfego.</summary>
    public static NetworkAdapterInfo? ReadAdapter()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                            && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                              && !g.Address.Equals(IPAddress.Any)))
                .OrderByDescending(n => n.Speed).FirstOrDefault();
            if (nic == null) return null;
            var props = nic.GetIPProperties();
            var gw = props.GatewayAddresses.First(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Address;
            string ip = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "—";
            string dns = props.DnsAddresses.FirstOrDefault(d => d.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "—";
            var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
            string mac = macBytes.Length > 0 ? string.Join("-", macBytes.Select(b => b.ToString("X2"))) : "—";
            return new NetworkAdapterInfo(nic.Name, nic.Description, AdapterType(nic.NetworkInterfaceType),
                nic.Speed > 0 ? nic.Speed / 1_000_000.0 : null, ip, gw, dns, mac);
        }
        catch { return null; }
    }

    public static async Task<NetworkResult> Run(Action<NetworkProgress> progress, CancellationToken ct)
    {
        progress(new(NetworkPhase.Preparing, 0, "Procurando a conexão ativa"));
        var adapter = ReadAdapter();
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
        const string downText = "Medindo a velocidade de download";
        progress(new(NetworkPhase.Download, LatencyEnd, downText, LatencyMs: netAvg));
        using var loadStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loaded = new List<double?>();
        var loadedPing = PingLoop(IPAddress.Parse("1.1.1.1"), 0, PingIntervalMs, loaded, loadStop.Token);
        var downT = new Transfer();
        double? down = null; string? netErr = null;
        var downTask = Download(downT, ct);
        try
        {
            await Track(downTask, () => progress(new(NetworkPhase.Download,
                LatencyEnd + (DownloadEnd - LatencyEnd) * Math.Min(1, downT.Clock.Elapsed.TotalSeconds / DownloadSeconds),
                downText, LatencyMs: netAvg, DownloadMbps: downT.Mbps())));
            down = await downTask;
        }
        catch (OperationCanceledException) { loadStop.Cancel(); throw; }
        catch (Exception ex) { netErr = ex.Message; }
        loadStop.Cancel();
        await loadedPing;
        var loadedSamples = Snapshot(loaded);
        var loadedRtts = loadedSamples.Where(v => v != null).Select(v => v!.Value).ToList();
        double? loadedMs = loadedRtts.Count >= 5 ? loadedRtts.Average() : null;

        // 4) Upload
        const string upText = "Medindo a velocidade de upload";
        progress(new(NetworkPhase.Upload, DownloadEnd, upText, LatencyMs: netAvg, DownloadMbps: down));
        var upT = new Transfer();
        double? up = null;
        var upTask = Upload(upT, ct);
        try
        {
            await Track(upTask, () => progress(new(NetworkPhase.Upload,
                DownloadEnd + (UploadEnd - DownloadEnd) * Math.Min(1, upT.Clock.Elapsed.TotalSeconds / UploadSeconds),
                upText, LatencyMs: netAvg, DownloadMbps: down, UploadMbps: upT.Mbps())));
            up = await upTask;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { netErr ??= ex.Message; }

        progress(new(NetworkPhase.Analysis, UploadEnd, "Analisando",
            LatencyMs: netAvg, DownloadMbps: down, UploadMbps: up));

        // ---- Veredito ----
        double netLoss = pings.Skip(1).Average(p => p.LossPct);
        double netJit = net.Count > 0 ? net.Max(p => p.Jitter) : 0;
        var warn = new List<string>();
        bool bad = false;

        bool lanBad = gwS.Answered && (gwS.LossPct > 2 || gwS.Avg > 30 || gwS.Jitter > 15);
        if (lanBad) warn.Add($"A rede local está instável (roteador: {gwS.Avg:0} ms, perda {gwS.LossPct:0.#}%, jitter {gwS.Jitter:0} ms). Se for Wi-Fi, aproxime-se do roteador ou use cabo.");
        if (!gwS.Answered) warn.Add("O roteador não responde a ping (alguns modelos bloqueiam). Isso por si só não é problema.");
        var internetVerdict = DiskVerdict.Good;
        if (net.Count == 0)
        {
            bad = !HasHttpEvidence(down, up);
            internetVerdict = bad ? DiskVerdict.Critical : DiskVerdict.Unknown;
            warn.Add(bad ? "Sem resposta ICMP ou HTTP dos alvos testados; verifique a conexão."
                : "HTTP funcionou, mas os alvos não responderam a ICMP. A latência e a perda até esses alvos são inconclusivas.");
        }
        else
        {
            if (netLoss > 5 || netAvg > 150) { bad = true; internetVerdict = DiskVerdict.Critical; }
            else if (netLoss > 1 || netAvg > 80 || netJit > 20) internetVerdict = DiskVerdict.Warning;
            if (netLoss > 1) warn.Add($"Há perda de pacotes até a internet ({netLoss:0.#}%). {(lanBad ? "Provavelmente vem da rede local." : "Como o roteador está bem, o problema deve estar na linha ou no provedor.")}");
            if (netAvg > 80) warn.Add($"Latência alta até a internet ({netAvg:0} ms).{(lanBad ? "" : " O roteador responde bem, então o atraso vem do provedor.")}");
            if (netJit > 20) warn.Add($"O jitter está alto ({netJit:0} ms): a latência oscila, o que causa engasgos em jogos online.");
        }
        double? bloat = loadedMs != null && net.Count > 0 ? loadedMs - net.Min(p => p.Avg) : null;
        if (bloat > 100)
            warn.Add($"A latência sobe para {loadedMs:0} ms quando a conexão está cheia (bufferbloat). Ative SQM/QoS no roteador ou limite a velocidade a ~90%.");
        if (dnsMs > 150) warn.Add($"A resolução do nome demorou {dnsMs:0} ms. O resultado pode incluir cache do Windows e atrasos da rede.");
        if (down is < 10) warn.Add($"Download baixo ({down:0.#} Mbps) para jogos, streaming e atualizações.");
        if (down == null && up == null) warn.Add("Não foi possível medir a velocidade (speed.cloudflare.com inacessível ou bloqueado).");
        else if (down == null) warn.Add($"Não foi possível medir o download{Reason(downT.Error ?? netErr)}. Se o teste foi repetido há pouco, o servidor pode estar limitando as conexões: tente de novo em alguns instantes.");
        else if (up == null) warn.Add($"Não foi possível medir o upload{Reason(upT.Error ?? netErr)}.");

        var verdict = bad ? DiskVerdict.Critical : warn.Any(w => !w.StartsWith("O roteador não responde")) ? DiskVerdict.Warning : DiskVerdict.Good;
        string label = verdict switch { DiskVerdict.Critical => "RUIM", DiskVerdict.Warning => "ATENÇÃO", _ => "ESTÁVEL" };
        const string stable = "Latência baixa, sem perda de pacotes e sem sinais de bufferbloat. Se o jogo ainda engasga, o problema provavelmente não é a conexão: veja o Gargalo e as temperaturas.";
        string reasons = warn.Count > 0 ? string.Join(" ", warn) : stable;

        var m = new List<KeyValuePair<string, string>>();
        foreach (var p in pings) m.Add(new($"{p.Name} ({p.Address})", p.Text));
        if (dnsMs != null) m.Add(new("Resolução de nome (pode usar cache)", $"{dnsMs:0} ms"));
        m.Add(new("Download", down != null ? $"{down:0.#} Mbps" : "—"));
        m.Add(new("Upload", up != null ? $"{up:0.#} Mbps" : "—"));
        if (loadedMs != null) m.Add(new("Latência sob carga", $"{loadedMs:0} ms (durante o download)"));
        m.Add(new("Adaptador", $"{adapter.Name} · {adapter.Kind} · link {adapter.LinkText}"));
        m.Add(new("IP / Gateway / DNS", $"{adapter.LocalIp} / {adapter.Gateway} / {adapter.Dns}"));

        var now = DateTime.Now;
        progress(new(NetworkPhase.Done, 1, "Concluído", LatencyMs: netAvg, DownloadMbps: down, UploadMbps: up));
        return new NetworkResult
        {
            Card = new DiskHealthCard
            {
                Title = "Diagnóstico de rede",
                Meta = $"{adapter.Kind} · testado às {now:HH:mm:ss}",
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
            LossPct = net.Count > 0 ? netLoss : null,
            DnsMs = dnsMs,
            DownloadMbps = down,
            UploadMbps = up,
            LoadedSamples = loadedSamples,
            LoadedLatencyMs = loadedMs,
            BufferbloatMs = bloat,
            LocalVerdict = !gwS.Answered ? DiskVerdict.Unknown : lanBad ? DiskVerdict.Warning : DiskVerdict.Good,
            InternetVerdict = internetVerdict,
            Notes = warn.Count > 0 ? warn : [stable],
            TestedAt = now,
        };
    }

    private static string Reason(string? error) => string.IsNullOrEmpty(error) ? "" : $" ({error})";

    internal static bool HasHttpEvidence(double? download, double? upload) => download is > 0 || upload is > 0;
}
