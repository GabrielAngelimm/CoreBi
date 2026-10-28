using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using CoreBi.Engine.Monitoring.Network;

internal static partial class Program
{
    private static void MlabTests()
    {
        Check("network: automatic fallback uses the second provider only after an incomplete test", () =>
        {
            var adapter = new NetworkAdapterInfo("test", "test", "Ethernet", 1000, "127.0.0.1", IPAddress.Loopback, "—", "—");
            NetworkResult Make(bool incomplete, bool connected = true) => new()
            {
                Card = new CoreBi.Engine.Monitoring.Storage.DiskHealthCard(),
                Adapter = connected ? adapter : null,
                InterruptedPhase = incomplete ? NetworkPhase.Download : null,
            };
            var calls = new List<NetworkSpeedProvider>();
            int switches = 0;
            var result = CoreBi.Desktop.Views.NetworkView.RunAutomatic(provider =>
            {
                calls.Add(provider);
                return Task.FromResult(Make(provider == NetworkSpeedProvider.Cloudflare));
            }, CancellationToken.None, () => switches++).GetAwaiter().GetResult();
            Require(result.InterruptedPhase == null && switches == 1 && calls.SequenceEqual(new[] { NetworkSpeedProvider.Cloudflare, NetworkSpeedProvider.Mlab }),
                "Fallback did not use the independent provider once");
            calls.Clear();
            CoreBi.Desktop.Views.NetworkView.RunAutomatic(provider => { calls.Add(provider); return Task.FromResult(Make(false)); },
                CancellationToken.None, () => switches++).GetAwaiter().GetResult();
            Require(calls.SequenceEqual(new[] { NetworkSpeedProvider.Cloudflare }) && switches == 1, "Successful test retried");
            calls.Clear();
            CoreBi.Desktop.Views.NetworkView.RunAutomatic(provider => { calls.Add(provider); return Task.FromResult(Make(true, false)); },
                CancellationToken.None, () => switches++).GetAwaiter().GetResult();
            Require(calls.Count == 1, "Fallback attempted without an adapter");
            return new { switchedOnce = true, successNotRepeated = true };
        });

        Check("network: M-Lab requires explicit consent before starting", () =>
        {
            Expect<InvalidOperationException>(() => NetworkTest.Run(_ => { }, CancellationToken.None,
                NetworkSpeedProvider.Mlab, mlabConsent: false));
            return new { consentRequired = true };
        });

        Check("network: M-Lab accepts only secure measurement servers", () =>
        {
            const string good = "wss://ndt.example.measurement-lab.org/ndt/v7/download?access_token=test";
            Require(MlabSpeedTest.ValidTarget(good, "/ndt/v7/download", out var parsed) && parsed != null,
                "Legitimate server rejected");
            Require(!MlabSpeedTest.ValidTarget("ws://ndt.example.measurement-lab.org/ndt/v7/download", "/ndt/v7/download", out _) &&
                !MlabSpeedTest.ValidTarget("wss://measurement-lab.org.attacker.test/ndt/v7/download", "/ndt/v7/download", out _) &&
                !MlabSpeedTest.ValidTarget("wss://user@ndt.example.measurement-lab.org/ndt/v7/download", "/ndt/v7/download", out _) &&
                !MlabSpeedTest.ValidTarget("wss://ndt.example.measurement-lab.org:444/ndt/v7/download", "/ndt/v7/download", out _) &&
                !MlabSpeedTest.ValidTarget("wss://ndt.example.measurement-lab.org/elsewhere", "/ndt/v7/download", out _),
                "Unsafe server accepted");
            return new { secureDestinations = true };
        });

        Check("network: Cloudflare cooldown does not block the independent M-Lab service", () =>
        {
            using var folder = new CaseFolder();
            var store = new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json"));
            var now = DateTimeOffset.UtcNow;
            store.Record(new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1)), now);
            Require(NetworkTest.ActiveSpeedLimit(NetworkSpeedProvider.Cloudflare, store, now) != null &&
                NetworkTest.ActiveSpeedLimit(NetworkSpeedProvider.Mlab, store, now) == null, "Cooldown applied to the wrong provider");
            return new { limitsAreIndependent = true };
        });

        Check("network: M-Lab locator selects matching servers and handles no capacity", () =>
        {
            const string body = "{\"results\":[{\"location\":{\"city\":\"São Paulo\"},\"urls\":{" +
                "\"wss:///ndt/v7/download\":\"wss://ndt.test.measurement-lab.org/ndt/v7/download?access_token=test\"," +
                "\"wss:///ndt/v7/upload\":\"wss://ndt.test.measurement-lab.org/ndt/v7/upload?access_token=test\"}}]}";
            using var client = new HttpClient(new MlabLocatorFixture(HttpStatusCode.OK, body));
            var target = MlabSpeedTest.Locate(client, CancellationToken.None).GetAwaiter().GetResult();
            Require(target.Location == "M-Lab · São Paulo" && target.Upload.Host == target.Download.Host,
                "Wrong server or location");
            using var busy = new HttpClient(new MlabLocatorFixture(HttpStatusCode.NoContent, ""));
            Expect<InvalidOperationException>(() => MlabSpeedTest.Locate(busy, CancellationToken.None).GetAwaiter().GetResult());
            using var invalid = new HttpClient(new MlabLocatorFixture(HttpStatusCode.OK,
                body.Replace("wss://ndt.test.measurement-lab.org/ndt/v7/upload", "wss://attacker.test/ndt/v7/upload")));
            Expect<InvalidOperationException>(() => MlabSpeedTest.Locate(invalid, CancellationToken.None).GetAwaiter().GetResult());
            return new { serverValidated = true, capacityHandled = true };
        });

        Check("network: M-Lab upload uses only valid server-confirmed measurements", () =>
        {
            var valid = Encoding.UTF8.GetBytes("{\"AppInfo\":{\"NumBytes\":10000000,\"ElapsedTime\":1000000}}");
            var sample = MlabSpeedTest.UploadMeasurement(valid, 20_000_000);
            Require(sample is { Bytes: 10_000_000, Speed: 80 }, "Incorrect acknowledged speed");
            Require(MlabSpeedTest.UploadMeasurement(valid, 1_000_000) == null &&
                MlabSpeedTest.UploadMeasurement(Encoding.UTF8.GetBytes("{\"AppInfo\":{\"NumBytes\":100,\"ElapsedTime\":0}}"), 20_000_000) == null &&
                MlabSpeedTest.UploadMeasurement(Encoding.UTF8.GetBytes("{\"AppInfo\":{\"NumBytes\":\"invalid\",\"ElapsedTime\":1000000}}"), 20_000_000) == null &&
                MlabSpeedTest.UploadMeasurement(Encoding.UTF8.GetBytes("invalid"), 20_000_000) == null,
                "Invalid server measurement accepted");
            return new { confirmedOnly = true };
        });

        Check("network: M-Lab accepts server TCP upload measurements and ignores idle samples", () =>
        {
            var json = Encoding.UTF8.GetBytes("{\"TCPInfo\":{\"BytesReceived\":100139268,\"ElapsedTime\":1542403}}");
            var sample = MlabSpeedTest.UploadMeasurement(json, 100_000_000);
            Require(sample is { IsTcp: true, Bytes: 100_139_268, Speed: > 500 and < 530 }, "TCP sample was discarded");
            Require(MlabSpeedTest.WithinSentLimit(sample.GetValueOrDefault().Bytes, 100_000_000, true) &&
                !MlabSpeedTest.WithinSentLimit(102_000_000, 100_000_000, true), "TCP overhead validation failed");
            var idle = (100_139_315L, 1_842_969L, 434.7, true);
            Require(!MlabSpeedTest.ShouldUseSample(idle, sample, completed: true), "Idle waiting reduced the upload result");
            var progress = (100_250_000L, 1_842_969L, 435.0, true);
            Require(MlabSpeedTest.ShouldUseSample(progress, sample, completed: true), "Late payload was ignored");
            return new { tcpConfirmed = true, idleIgnored = true };
        });

        Check("network: NDT7 loopback supports repeated download and confirmed upload", () =>
        {
            using var server = new LocalNdtServer();
            var first = new NetworkTest.Transfer();
            var second = new NetworkTest.Transfer();
            double? firstSpeed = MlabSpeedTest.Download(first, server.Download, CancellationToken.None, 16_000_000).GetAwaiter().GetResult();
            double? secondSpeed = MlabSpeedTest.Download(second, server.Download, CancellationToken.None, 16_000_000).GetAwaiter().GetResult();
            var upload = new NetworkTest.Transfer();
            double? uploadSpeed = MlabSpeedTest.Upload(upload, server.Upload, CancellationToken.None, 24_000_000).GetAwaiter().GetResult();
            Require(firstSpeed is > 1 && secondSpeed is > 1 && uploadSpeed is > 1 && upload.Bytes > 0,
                $"NDT7 loopback failed: down={firstSpeed}/{secondSpeed}, up={uploadSpeed}, error={upload.Error}");
            Require(first.Bytes is >= 16_000_000 and <= 16_065_536 && second.Bytes is >= 16_000_000 and <= 16_065_536 &&
                upload.Bytes <= 24_000_000 && server.Downloads == 2 && server.Uploads == 1,
                "Unexpected transfer budget or request count");
            return new { downloads = server.Downloads, uploads = server.Uploads, uploadConfirmed = true };
        });

        Check("network: cancelling an NDT7 transfer stops the operation", () =>
        {
            using var server = new LocalNdtServer();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            Expect<OperationCanceledException>(() => MlabSpeedTest.Download(new NetworkTest.Transfer(), server.Download,
                cancel.Token, 64_000_000).GetAwaiter().GetResult());
            return new { cancelled = true };
        });

        Check("network: NDT7 upload succeeds when the server sends only TCPInfo", () =>
        {
            using var server = new LocalNdtServer(tcpOnly: true);
            var transfer = new NetworkTest.Transfer();
            var speed = MlabSpeedTest.Upload(transfer, server.Upload, CancellationToken.None, 24_000_000).GetAwaiter().GetResult();
            Require(speed is > 1 && transfer.IncludesTcpOverhead && transfer.Bytes > 0,
                $"TCP upload confirmation was not used: {transfer.Error}");
            return new { tcpConfirmed = true };
        });
    }

    private sealed class MlabLocatorFixture(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class LocalNdtServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accepting;
        private readonly System.Collections.Concurrent.ConcurrentBag<Task> _connections = new();
        private int _downloads, _uploads;
        public int Downloads => Volatile.Read(ref _downloads);
        public int Uploads => Volatile.Read(ref _uploads);
        public Uri Download { get; }
        public Uri Upload { get; }

        private readonly bool _tcpOnly;
        public LocalNdtServer(bool tcpOnly = false)
        {
            _tcpOnly = tcpOnly;
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            int number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            _listener.Prefixes.Add($"http://127.0.0.1:{number}/");
            _listener.Start();
            Download = new Uri($"ws://127.0.0.1:{number}/ndt/v7/download");
            Upload = new Uri($"ws://127.0.0.1:{number}/ndt/v7/upload");
            _accepting = Accept();
        }

        private async Task Accept()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                    _connections.Add(Serve(context));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
        }

        private async Task Serve(HttpListenerContext context)
        {
            try
            {
                var accepted = await context.AcceptWebSocketAsync("net.measurementlab.ndt.v7");
                using var socket = accepted.WebSocket;
                var bytes = new byte[65_536];
                if (context.Request.Url!.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref _downloads);
                    while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                    {
                        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Binary, true, _stop.Token);
                        await Task.Delay(5, _stop.Token);
                    }
                }
                else
                {
                    Interlocked.Increment(ref _uploads);
                    long received = 0, lastReport = 0;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                    {
                        var part = await socket.ReceiveAsync(bytes.AsMemory(), _stop.Token);
                        if (part.MessageType == WebSocketMessageType.Close) break;
                        if (part.MessageType != WebSocketMessageType.Binary) continue;
                        received += part.Count;
                        if (clock.ElapsedMilliseconds >= 350 && clock.ElapsedMilliseconds - lastReport >= 200)
                        {
                            lastReport = clock.ElapsedMilliseconds;
                            string field = _tcpOnly ? "TCPInfo" : "AppInfo";
                            string byteField = _tcpOnly ? "BytesReceived" : "NumBytes";
                            var data = Encoding.UTF8.GetBytes($"{{\"{field}\":{{\"{byteField}\":{received},\"ElapsedTime\":{lastReport * 1000}}}}}");
                            await socket.SendAsync(data.AsMemory(), WebSocketMessageType.Text, true, _stop.Token);
                        }
                        await Task.Delay(5, _stop.Token);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or WebSocketException or OperationCanceledException or IOException or ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _accepting.GetAwaiter().GetResult();
            Task.WhenAll(_connections.ToArray()).GetAwaiter().GetResult();
            _listener.Close();
            _stop.Dispose();
        }
    }
}
