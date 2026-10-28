using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using CoreBi.Engine.Monitoring.Network;

internal static partial class Program
{
    private static void NetworkTests()
    {
        Check("network: download excludes warmup only when data continues", () =>
        {
            double expected = 80_000_000 * 8 / 1e6 / 7;
            Require(Math.Abs(NetworkTest.CalculateMbps(100_000_000, 8, 20_000_000, 1)!.Value - expected) < 0.001,
                "Warmup bytes and duration were not excluded together");
            Require(NetworkTest.CalculateMbps(20_000_000, 8, 20_000_000, 1) == 20,
                "A stalled transfer should retain its full elapsed time");
            Require(NetworkTest.CalculateMbps(0, 8, 0, 0) == null, "Empty transfer produced a speed");
            return new { warmupAndStall = true };
        });

        Check("network: loaded latency compares the same target and requires a download", () =>
        {
            var transfer = new NetworkTest.Transfer();
            Require(!transfer.HasNewBytes(), "Idle period considered active traffic");
            Interlocked.Add(ref transfer.Bytes, 100);
            Require(transfer.HasNewBytes() && !transfer.HasNewBytes(), "Download activity was not sampled by interval");
            var cloudflare = new PingResult("Cloudflare", "1.1.1.1", new double?[] { 10, 20 });
            var silent = new PingResult("Cloudflare", "1.1.1.1", new double?[] { null, null });
            var samples = new double[] { 40, 41, 42, 43, 44 };
            Require(NetworkTest.LoadedLatency(null, samples) == null, "Pings without a download treated as loaded");
            Require(NetworkTest.LoadedLatency(1, samples.Take(4).ToArray()) == null, "Too few pings accepted");
            var loaded = NetworkTest.LoadedLatency(1, samples);
            Require(loaded == 42 && NetworkTest.Bufferbloat(cloudflare, loaded) == 27, "Wrong idle baseline");
            Require(NetworkTest.Bufferbloat(silent, loaded) == null, "A silent baseline produced a comparison");
            return new { loaded, increase = 27 };
        });

        Check("network: a silent ICMP target does not become 100 percent loss", () =>
        {
            var answered = new PingResult("Answered", "1.1.1.1", new double?[] { 10, null, 10, 10 });
            var silent = new PingResult("Silent", "8.8.8.8", new double?[] { null, null, null, null });
            Require(NetworkTest.InternetLoss(new[] { answered, silent }) == 25, "Silent target distorted loss");
            Require(NetworkTest.InternetLoss(new[] { silent }) == null, "Silent targets produced a loss estimate");
            return new { silentIsInconclusive = true };
        });

        Check("network: upload waits for in-flight accepted blocks and counts them", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new UploadFixture(TimeSpan.FromMilliseconds(500), HttpStatusCode.OK);
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Upload(transfer, client, limit, TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(700), 2,
                CancellationToken.None).GetAwaiter().GetResult();
            Require(handler.Calls == 2 && transfer.Bytes == handler.AcceptedBytes && transfer.Bytes == 128_000,
                "Accepted in-flight uploads were lost or counted twice");
            Require(speed is > 0 && transfer.Clock.Elapsed.TotalMilliseconds >= 500,
                "Upload was not measured through the accepted responses");
            return new { transfer.Bytes, elapsedMs = transfer.Clock.ElapsedMilliseconds };
        });

        Check("network: upload grows its blocks without issuing excessive short requests", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new AdaptiveUploadFixture();
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Upload(transfer, client, limit, TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(400), 1,
                CancellationToken.None).GetAwaiter().GetResult();
            Require(speed is > 0 && handler.Largest > 5_000_000 && handler.Largest <= 25_000_000 && handler.Calls <= 8,
                "Upload did not adapt its block size");
            return new { handler.Calls, handler.Largest };
        });

        Check("network: upload respects the shared byte budget", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new UploadFixture(TimeSpan.FromMilliseconds(200), HttpStatusCode.OK);
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Upload(transfer, client, limit, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), 1,
                CancellationToken.None, byteBudget: 300_000).GetAwaiter().GetResult();
            Require(speed is > 0 && transfer.Bytes == 300_000 && handler.AcceptedBytes == 300_000 && handler.Calls == 2,
                "Upload did not stop at the budget");
            return new { transfer.Bytes, handler.Calls };
        });

        Check("network: failed or unfinished uploads are not presented as complete", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var rejectedHandler = new UploadFixture(TimeSpan.Zero, HttpStatusCode.ServiceUnavailable);
            using var rejectedClient = new HttpClient(rejectedHandler);
            var rejected = new NetworkTest.Transfer();
            var speed = NetworkTest.Upload(rejected, rejectedClient, limit, TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(200), 1,
                CancellationToken.None).GetAwaiter().GetResult();
            Require(speed == null && rejected.Bytes == 0 && rejected.Error != null, "Rejected data counted as accepted");

            using var incompleteHandler = new UploadFixture(TimeSpan.Zero, HttpStatusCode.OK, stallAfterFirst: true);
            using var incompleteClient = new HttpClient(incompleteHandler);
            var incomplete = new NetworkTest.Transfer();
            var partial = NetworkTest.Upload(incomplete, incompleteClient, limit, TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(250), 1,
                CancellationToken.None).GetAwaiter().GetResult();
            Require(partial is > 0 && incomplete.Incomplete && incomplete.Bytes == incompleteHandler.AcceptedBytes,
                "An unfinished transfer was not identified");
            return new { rejected = true, partialFlagged = true };
        });

        Check("network: cancelling upload stops the work", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new UploadFixture(TimeSpan.FromSeconds(5), HttpStatusCode.OK);
            using var client = new HttpClient(handler);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var transfer = new NetworkTest.Transfer();
            Expect<OperationCanceledException>(() => NetworkTest.Upload(transfer, client, limit, TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(1), 2, cancel.Token).GetAwaiter().GetResult());
            Require(transfer.Bytes == 0, "Cancelled requests were counted");
            return new { cancelled = true };
        });

        Check("network: 429 stops all download workers and persists the server wait", () =>
        {
            using var folder = new CaseFolder();
            string path = Path.Combine(folder.Path, "cooldown.json");
            var store = new NetworkRateLimitStore(path);
            using var limit = new NetworkRateLimitSession(store);
            using var handler = new RateLimitFixture();
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Download(transfer, client, limit, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100), 4,
                CancellationToken.None).GetAwaiter().GetResult();
            var retry = store.Active(DateTimeOffset.UtcNow);
            Require(speed == null && limit.Limited && handler.Calls is >= 1 and <= 4,
                "429 was retried or produced a partial speed");
            Require(retry != null && retry.Value - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(59), "Retry-After was ignored");
            Require(new NetworkRateLimitStore(path).Active(DateTimeOffset.UtcNow) == retry, "Wait was lost on restart");
            return new { handler.Calls, persisted = true };
        });

        Check("network: 429 also interrupts upload and never counts rejected data", () =>
        {
            using var folder = new CaseFolder();
            var store = new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json"));
            using var limit = new NetworkRateLimitSession(store);
            using var handler = new RateLimitFixture();
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Upload(transfer, client, limit, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 3,
                CancellationToken.None).GetAwaiter().GetResult();
            Require(speed == null && transfer.Bytes == 0 && limit.Limited && handler.Calls is >= 1 and <= 3,
                "An upload continued after rate limiting or counted rejected bytes");
            Require(store.Active(DateTimeOffset.UtcNow) != null, "Upload rate limit was not stored");
            return new { handler.Calls, noRejectedBytes = true };
        });

        Check("network: partial download is not reported as a final speed after 429", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new PartialThenRateLimitFixture();
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Download(transfer, client, limit, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100), 1,
                CancellationToken.None).GetAwaiter().GetResult();
            Require(transfer.Bytes == 1024 && speed == null && limit.Limited && handler.Calls == 2,
                "Partial bytes were shown as a completed speed");
            return new { handler.Calls, partialDiscarded = true };
        });

        Check("network: cooldown blocks another run without contacting the server", () =>
        {
            using var folder = new CaseFolder();
            var store = new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json"));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            store.Record(new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)), now);
            int clients = 0;
            var adapter = new NetworkAdapterInfo("test", "test", "Ethernet", 1000, "127.0.0.1", IPAddress.Loopback, "—", "—");
            var result = NetworkTest.Run(_ => { }, CancellationToken.None, store,
                () => { clients++; return new HttpClient(new RateLimitFixture()); }, () => adapter).GetAwaiter().GetResult();
            Require(clients == 0 && result.RetryAfterUtc != null && result.InterruptedPhase == NetworkPhase.Preparing,
                "A new run contacted the server during its requested wait");
            Require(result.DownloadMbps == null && result.UploadMbps == null, "Blocked run invented speed results");
            return new { noNetworkRequests = true };
        });

        Check("network: retry delay handles HTTP dates, missing headers and longer concurrent limits", () =>
        {
            using var folder = new CaseFolder();
            var store = new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json"));
            var now = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
            Require(store.Record(null, now) == now.AddSeconds(60), "Missing header fallback");
            Require(store.Record(new RetryConditionHeaderValue(now.AddMinutes(10)), now) == now.AddMinutes(10), "HTTP date ignored");
            Require(store.Record(new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)), now) == now.AddMinutes(10), "Longer limit shortened");
            Require(store.Active(now.AddMinutes(11)) == null, "Expired wait still active");
            return new { deltaAndDate = true };
        });

        Check("network: short or empty downloads cannot trigger an endless request loop", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new EmptyDownloadFixture();
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Download(transfer, client, limit, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100), 1,
                CancellationToken.None).GetAwaiter().GetResult();
            Require(speed == null && handler.Calls == 3 && transfer.Error != null, "Empty responses were retried without a bound");
            return new { handler.Calls, noInventedSpeed = true };
        });

        Check("network: forbidden download is not retried and has a clear reason", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new ForbiddenDownloadFixture();
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Download(transfer, client, limit, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100), 1,
                CancellationToken.None).GetAwaiter().GetResult();
            Require(speed == null && handler.Calls == 1 && transfer.Error == "servidor recusou a solicitação",
                "A permanent refusal was retried or not explained");
            return new { handler.Calls, clearReason = true };
        });

        Check("network: repeated runs use long download streams without excessive requests", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new StreamingDownloadFixture();
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Download(transfer, client, limit, TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(100), 4,
                CancellationToken.None).GetAwaiter().GetResult();
            var again = NetworkTest.Download(new NetworkTest.Transfer(), client, limit, TimeSpan.FromMilliseconds(800),
                TimeSpan.FromMilliseconds(100), 4, CancellationToken.None).GetAwaiter().GetResult();
            Require(handler.Calls == 8 && handler.RequestsCorrect && speed is > 50 and < 1000 && again is > 50 and < 1000,
                "Repeated downloads did not use four large streams per run");
            return new { handler.Calls, speedMbps = Math.Round(speed.GetValueOrDefault()) };
        });

        Check("network: download stops at the shared byte budget", () =>
        {
            using var folder = new CaseFolder();
            using var limit = new NetworkRateLimitSession(new NetworkRateLimitStore(Path.Combine(folder.Path, "cooldown.json")));
            using var handler = new StreamingDownloadFixture();
            using var client = new HttpClient(handler);
            var transfer = new NetworkTest.Transfer();
            var speed = NetworkTest.Download(transfer, client, limit, TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(100), 4,
                CancellationToken.None, byteBudget: 16_000_000).GetAwaiter().GetResult();
            Require(speed is > 0 && transfer.Bytes is >= 16_000_000 and <= 16_327_680 && transfer.Clock.Elapsed.TotalSeconds < 4,
                "Download did not stop within the shared budget");
            Require(transfer.Error == null && !limit.Limited, "Budget completion was treated as a failure");
            return new { transfer.Bytes, elapsedMs = transfer.Clock.ElapsedMilliseconds };
        });
        NetworkLoopbackTest();
    }

    private sealed class UploadFixture(TimeSpan delay, HttpStatusCode status, bool stallAfterFirst = false) : HttpMessageHandler
    {
        private int _calls;
        private long _acceptedBytes;
        public int Calls => Volatile.Read(ref _calls);
        public long AcceptedBytes => Interlocked.Read(ref _acceptedBytes);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _calls);
            byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            if (stallAfterFirst && call > 1) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            else await Task.Delay(delay, cancellationToken);
            if (status == HttpStatusCode.OK) Interlocked.Add(ref _acceptedBytes, body.Length);
            return new HttpResponseMessage(status);
        }
    }

    private sealed class RateLimitFixture : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }
    }

    private sealed class AdaptiveUploadFixture : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public long Largest { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Largest = Math.Max(Largest, request.Content?.Headers.ContentLength ?? 0);
            await Task.Delay(60, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class PartialThenRateLimitFixture : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1024]) });
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }
    }

    private sealed class EmptyDownloadFixture : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        }
    }

    private sealed class ForbiddenDownloadFixture : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }
    }

    private sealed class StreamingDownloadFixture : HttpMessageHandler
    {
        private int _calls, _invalid;
        public int Calls => Volatile.Read(ref _calls);
        public bool RequestsCorrect => Volatile.Read(ref _invalid) == 0;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (request.RequestUri?.Query != "?bytes=50000000") Interlocked.Increment(ref _invalid);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SimulatedStream()) });
        }
    }

    private sealed class SimulatedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken);
            return buffer.Length;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
