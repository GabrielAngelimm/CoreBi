using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;

namespace CoreBi.Engine.Monitoring.Network;

/// <summary>Cliente NDT7 para o serviço público do M-Lab. A integração deve obter consentimento informado.</summary>
internal static class MlabSpeedTest
{
    private const string Protocol = "net.measurementlab.ndt.v7";
    private const int MaxLocateBytes = 65_536;
    private const long DownloadBudget = 350_000_000, UploadBudget = 200_000_000;
    private static readonly Uri LocateUri = new("https://locate.measurementlab.net/v2/nearest/ndt/ndt7");
    internal sealed record Target(Uri Download, Uri Upload, string Location);

    internal static async Task<Target> Locate(HttpClient client, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LocateUri);
        request.Headers.UserAgent.ParseAdd("CoreBi/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NoContent)
            throw new InvalidOperationException("O M-Lab está sem capacidade ou atingiu o limite de testes. Tente mais tarde.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxLocateBytes)
            throw new InvalidOperationException("O M-Lab enviou uma resposta de seleção de servidor inválida.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + read > MaxLocateBytes)
                throw new InvalidOperationException("O M-Lab enviou uma resposta de seleção de servidor inválida.");
            body.Write(buffer, 0, read);
        }
        try
        {
            using var json = JsonDocument.Parse(body.ToArray());
            if (!json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("O M-Lab não retornou um servidor disponível.");
            foreach (var result in results.EnumerateArray())
            {
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("urls", out var urls) || urls.ValueKind != JsonValueKind.Object ||
                    !urls.TryGetProperty("wss:///ndt/v7/download", out var down) ||
                    !urls.TryGetProperty("wss:///ndt/v7/upload", out var up) ||
                    down.ValueKind != JsonValueKind.String || up.ValueKind != JsonValueKind.String ||
                    !ValidTarget(down.GetString(), "/ndt/v7/download", out var download) ||
                    !ValidTarget(up.GetString(), "/ndt/v7/upload", out var upload) ||
                    !string.Equals(download!.Host, upload!.Host, StringComparison.OrdinalIgnoreCase)) continue;
                string location = "M-Lab";
                if (result.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object &&
                    loc.TryGetProperty("city", out var city) && city.ValueKind == JsonValueKind.String)
                {
                    string name = city.GetString() ?? "";
                    if (name.Length is > 0 and <= 60 && name.All(c => !char.IsControl(c))) location = $"M-Lab · {name}";
                }
                return new(download, upload, location);
            }
        }
        catch (JsonException) { throw new InvalidOperationException("O M-Lab enviou uma resposta de seleção de servidor inválida."); }
        throw new InvalidOperationException("O M-Lab não retornou um servidor seguro disponível.");
    }

    internal static bool ValidTarget(string? value, string path, out Uri? uri)
    {
        uri = null;
        if (value is not { Length: > 0 and <= 4096 } || !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != "wss" || parsed.Port != 443 || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0 ||
            !parsed.Host.EndsWith(".measurement-lab.org", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.AbsolutePath, path, StringComparison.Ordinal)) return false;
        uri = parsed;
        return true;
    }

    private static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(Protocol);
        socket.Options.SetRequestHeader("User-Agent", "CoreBi/1.0");
        return socket;
    }

    private static HttpMessageInvoker CreateTransport() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    });

    internal static async Task<double?> Download(NetworkTest.Transfer transfer, Uri url, CancellationToken ct,
        long byteBudget = DownloadBudget)
    {
        ct.ThrowIfCancellationRequested();
        if (byteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(byteBudget));
        using var socket = CreateSocket();
        using var transport = CreateTransport();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(23)); // até 10 s de conexão e 13 s de teste
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(url, transport, connect.Token);
            }
            transfer.Clock.Restart();
            using var phase = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            phase.CancelAfter(TimeSpan.FromSeconds(13));
            var warmup = Task.Run(async () =>
            {
                try { await Task.Delay(1000, phase.Token); transfer.FinishWarmup(); }
                catch (OperationCanceledException) { }
            });
            var buffer = new byte[65_536];
            while (socket.State == WebSocketState.Open && !phase.IsCancellationRequested)
            {
                var message = await socket.ReceiveAsync(buffer.AsMemory(), phase.Token);
                if (message.MessageType == WebSocketMessageType.Close) break;
                if (message.MessageType != WebSocketMessageType.Binary) continue;
                if (Interlocked.Add(ref transfer.Bytes, message.Count) >= byteBudget) break;
            }
            transfer.Clock.Stop();
            phase.Cancel();
            await warmup;
            await CloseOutput(socket, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { transfer.Incomplete = true; transfer.Error = "tempo de espera esgotado"; }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or InvalidOperationException)
        { transfer.Incomplete = true; transfer.Error = "falha de conexão com o M-Lab"; }
        finally { transfer.Clock.Stop(); }
        ct.ThrowIfCancellationRequested();
        return transfer.Mbps();
    }

    internal static async Task<double?> Upload(NetworkTest.Transfer transfer, Uri url, CancellationToken ct,
        long byteBudget = UploadBudget)
    {
        ct.ThrowIfCancellationRequested();
        if (byteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(byteBudget));
        using var socket = CreateSocket();
        using var transport = CreateTransport();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(23));
        double? measured = null;
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(url, transport, connect.Token);
            }
            transfer.Clock.Restart();
            using var phase = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            phase.CancelAfter(TimeSpan.FromSeconds(13));
            var sent = new UploadSent();
            var receiving = ReceiveUploadMeasurements(socket, transfer, sent, byteBudget, phase.Token);
            var payload = new byte[1_048_576];
            new Random(1).NextBytes(payload);
            long reserved = 0;
            int size = 8192;
            try
            {
                while (socket.State == WebSocketState.Open && !phase.IsCancellationRequested &&
                       transfer.Clock.Elapsed < TimeSpan.FromSeconds(10) && reserved + 1024 <= byteBudget)
                {
                    int count = (int)Math.Min(size, byteBudget - reserved);
                    reserved += count;
                    Interlocked.Exchange(ref sent.MaximumBytes, reserved);
                    var watch = Stopwatch.StartNew();
                    await socket.SendAsync(payload.AsMemory(0, count), WebSocketMessageType.Binary, true, phase.Token);
                    if (watch.ElapsedMilliseconds < 100) size = Math.Min(size * 2, payload.Length);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or InvalidOperationException && receiving.IsCompleted) { }
            finally
            {
                Volatile.Write(ref sent.Completed, 1);
                try
                {
                    if (!phase.IsCancellationRequested && !receiving.IsCompleted)
                        await Task.WhenAny(receiving, Task.Delay(TimeSpan.FromSeconds(2), phase.Token));
                    if (!phase.IsCancellationRequested) await CloseOutput(socket, ct);
                }
                finally
                {
                    phase.Cancel();
                    measured = await receiving;
                }
            }
            transfer.Clock.Stop();
            if (measured == null) { transfer.Incomplete = true; transfer.Error = "o servidor não confirmou o upload"; }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { transfer.Incomplete = true; transfer.Error = "tempo de espera esgotado"; }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or InvalidOperationException)
        { transfer.Incomplete = true; transfer.Error = "falha de conexão com o M-Lab"; }
        finally { transfer.Clock.Stop(); }
        ct.ThrowIfCancellationRequested();
        return measured;
    }

    private sealed class UploadSent { public long MaximumBytes; public int Completed; }

    private static async Task<double?> ReceiveUploadMeasurements(WebSocket socket, NetworkTest.Transfer transfer,
        UploadSent sent, long byteBudget, CancellationToken ct)
    {
        var buffer = new byte[16_384];
        using var message = new MemoryStream();
        (long Bytes, long Microseconds, double Speed, bool IsTcp)? latest = null;
        try
        {
            while (!ct.IsCancellationRequested && socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var part = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                if (part.MessageType == WebSocketMessageType.Close) break;
                if (part.MessageType != WebSocketMessageType.Text) throw new WebSocketException("O servidor enviou dados de upload inválidos.");
                if (message.Length + part.Count > MaxLocateBytes) throw new WebSocketException("Mensagem de medição excessiva.");
                message.Write(buffer, 0, part.Count);
                if (!part.EndOfMessage) continue;
                if (UploadMeasurement(message.ToArray(), byteBudget) is { } sample &&
                    WithinSentLimit(sample.Bytes, Interlocked.Read(ref sent.MaximumBytes), sample.IsTcp) &&
                    ShouldUseSample(sample, latest, Volatile.Read(ref sent.Completed) != 0))
                {
                    Interlocked.Exchange(ref transfer.Bytes, sample.Bytes);
                    transfer.IncludesTcpOverhead = sample.IsTcp;
                    latest = sample;
                }
                message.SetLength(0);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        return latest?.Speed;
    }

    private static async Task CloseOutput(ClientWebSocket socket, CancellationToken ct)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        closing.CancelAfter(TimeSpan.FromSeconds(1));
        try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", closing.Token); }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidOperationException) { }
        ct.ThrowIfCancellationRequested();
    }

    internal static bool WithinSentLimit(long bytes, long sent, bool tcp) =>
        bytes <= sent || tcp && bytes - sent <= Math.Max(65_536, sent / 100);

    internal static bool ShouldUseSample((long Bytes, long Microseconds, double Speed, bool IsTcp) sample,
        (long Bytes, long Microseconds, double Speed, bool IsTcp)? previous, bool completed)
    {
        if (previous == null) return true;
        if (previous.Value.IsTcp && !sample.IsTcp) return true;
        return sample.IsTcp == previous.Value.IsTcp && sample.Bytes >= previous.Value.Bytes &&
            sample.Microseconds > previous.Value.Microseconds &&
            (!completed || sample.Bytes - previous.Value.Bytes >= 65_536);
    }

    internal static (long Bytes, long Microseconds, double Speed, bool IsTcp)? UploadMeasurement(byte[] json, long byteBudget)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("AppInfo", out var app) && ReadSample(app, "NumBytes", byteBudget, false) is { } application)
                return application;
            return root.TryGetProperty("TCPInfo", out var tcp) ? ReadSample(tcp, "BytesReceived", byteBudget, true) : null;
        }
        catch (JsonException) { return null; }
    }

    private static (long Bytes, long Microseconds, double Speed, bool IsTcp)? ReadSample(JsonElement info,
        string bytesField, long byteBudget, bool isTcp)
    {
        if (info.ValueKind != JsonValueKind.Object ||
            !info.TryGetProperty(bytesField, out var bytes) || bytes.ValueKind != JsonValueKind.Number || !bytes.TryGetInt64(out long count) ||
            !info.TryGetProperty("ElapsedTime", out var elapsed) || elapsed.ValueKind != JsonValueKind.Number || !elapsed.TryGetInt64(out long microseconds) ||
            count <= 0 || !WithinSentLimit(count, byteBudget, isTcp) || microseconds < 300_000 || microseconds > 13_000_000) return null;
        return (count, microseconds, count * 8.0 / microseconds, isTcp);
    }
}
