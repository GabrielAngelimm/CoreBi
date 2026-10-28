using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using CoreBi.Engine.Monitoring.Network;

internal static partial class Program
{
    private static void NetworkLoopbackTest()
    {
        Check("network: real loopback HTTP supports consecutive downloads and an upload", () =>
        {
            using var folder = new CaseFolder();
            using var server = new LocalSpeedServer();
            using var client = new HttpClient(new LocalSpeedHandler(server.Address));
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            var duration = TimeSpan.FromSeconds(8);
            var warmup = TimeSpan.FromMilliseconds(100);
            var firstTransfer = new NetworkTest.Transfer();
            var first = NetworkTest.Download(firstTransfer, client, limit, duration, warmup, 4, CancellationToken.None, byteBudget: 16_000_000)
                .GetAwaiter().GetResult();
            var secondTransfer = new NetworkTest.Transfer();
            var second = NetworkTest.Download(secondTransfer, client, limit, duration, warmup, 4, CancellationToken.None, byteBudget: 16_000_000)
                .GetAwaiter().GetResult();
            var uploadTransfer = new NetworkTest.Transfer();
            var upload = NetworkTest.Upload(uploadTransfer, client, limit, duration, TimeSpan.FromSeconds(2), 3,
                CancellationToken.None, byteBudget: 20_000_000).GetAwaiter().GetResult();
            Require(first is > 10 && second is > 10 && upload is > 10 && !limit.Limited, "Loopback transfers did not complete");
            Require(firstTransfer.Bytes is >= 16_000_000 and <= 16_327_680 && secondTransfer.Bytes is >= 16_000_000 and <= 16_327_680
                && uploadTransfer.Bytes == 20_000_000, $"Unexpected payloads: {firstTransfer.Bytes}, {secondTransfer.Bytes}, {uploadTransfer.Bytes}");
            Require(server.DownloadRequests == 8 && server.UploadRequests >= 3 && server.BadRequests == 0,
                "Unexpected requests in the real HTTP exchange");
            return new { downloads = server.DownloadRequests, uploads = server.UploadRequests, repeatable = true };
        });
    }

    private sealed class LocalSpeedHandler(Uri address) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "speed.cloudflare.com") throw new InvalidOperationException("Unexpected test target");
            request.RequestUri = new Uri(address, request.RequestUri.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class LocalSpeedServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _connections = new();
        private readonly Task _accepting;
        private int _downloads, _uploads, _bad;
        public int DownloadRequests => Volatile.Read(ref _downloads);
        public int UploadRequests => Volatile.Read(ref _uploads);
        public int BadRequests => Volatile.Read(ref _bad);
        public Uri Address { get; }

        public LocalSpeedServer()
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _accepting = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _connections.Add(ServeAsync(client, _stop.Token));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
                try
                {
                    using var stream = client.GetStream();
                    var header = new byte[16_384];
                    int count = 0, end = -1;
                    while (end < 0 && count < header.Length)
                    {
                        int read = await stream.ReadAsync(header.AsMemory(count), ct);
                        if (read == 0) return;
                        count += read;
                        for (int i = 0; i <= count - 4; i++)
                            if (header[i] == 13 && header[i + 1] == 10 && header[i + 2] == 13 && header[i + 3] == 10) { end = i + 4; break; }
                    }
                    if (end < 0) { Interlocked.Increment(ref _bad); return; }
                    string text = Encoding.ASCII.GetString(header, 0, end);
                    if (text.StartsWith("GET /__down?bytes=50000000 ", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref _downloads);
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 50000000\r\nConnection: close\r\n\r\n"), ct);
                        var block = new byte[65_536];
                        for (int remaining = 50_000_000; remaining > 0;)
                        {
                            int length = Math.Min(remaining, block.Length);
                            await stream.WriteAsync(block.AsMemory(0, length), ct);
                            remaining -= length;
                            await Task.Delay(8, ct);
                        }
                    }
                    else if (text.StartsWith("POST /__up ", StringComparison.Ordinal))
                    {
                        var line = text.Split("\r\n").FirstOrDefault(s => s.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                        if (line == null || !long.TryParse(line[(line.IndexOf(':') + 1)..].Trim(), out long length))
                        { Interlocked.Increment(ref _bad); return; }
                        Interlocked.Increment(ref _uploads);
                        long received = count - end;
                        var block = new byte[65_536];
                        while (received < length)
                        {
                            int read = await stream.ReadAsync(block.AsMemory(0, (int)Math.Min(block.Length, length - received)), ct);
                            if (read == 0) return;
                            received += read;
                            await Task.Delay(8, ct);
                        }
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
                    }
                    else Interlocked.Increment(ref _bad);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _accepting.GetAwaiter().GetResult();
            Task.WhenAll(_connections.ToArray()).GetAwaiter().GetResult();
            _stop.Dispose();
        }
    }
}
