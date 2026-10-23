using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Games;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Performance.Benchmarks;
using CoreBi.Engine.Tools.Apps;
using CoreBi.Engine.Tools.Drivers;
using CoreBi.Engine.Tools.Startup;
using CoreBi.Engine.Persistence;
using LibreHardwareMonitor.Hardware;

internal static partial class Program
{
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    private static void Hardening()
    {
        Check("settings: null collections and invalid scale recover safely", () =>
        {
            string file = Path.Combine(AppSettings.Folder, "settings.json");
            File.WriteAllText(file, "{\"BenchRuns\":null,\"BenchRecords\":null,\"StartupRemoved\":null,\"OverlayScale\":999,\"OverlayCorner\":\"bad\"}");
            var settings = AppSettings.Load();
            Require(settings.BenchRuns.Count == 0 && settings.BenchRecords.Count == 0 && settings.StartupRemoved.Count == 0, "Null lists survived");
            Require(settings.OverlayScale == 1 && settings.OverlayCorner == "TL", "Invalid overlay settings survived");
            return new { normalized = true };
        });
        Check("atomic JSON: failed serialization preserves old file and removes temporary", () =>
        {
            using var test = new CaseFolder(); string file = Path.Combine(test.Path, "state.json"); File.WriteAllText(file, "old-state");
            Expect<ArgumentException>(() => AtomicJsonFile.Write(file, new { invalid = double.NaN }));
            Require(File.ReadAllText(file) == "old-state", "Old file was lost");
            Require(Directory.GetFiles(test.Path, "*.tmp").Length == 0, "Partial temporary left behind");
            return new { oldStatePreserved = true };
        });
        Check("sensors: closing while opening releases exactly once", () =>
        {
            using var source = new BlockingHardwareSource(); using var monitor = new SensorMonitor(source);
            Task open = monitor.OpenAsync(); Require(source.Entered.Wait(3000), "Open did not begin");
            Require(ReferenceEquals(open, monitor.OpenAsync()), "Duplicate hardware initialization");
            monitor.Dispose(); source.Release.Set(); open.GetAwaiter().GetResult(); monitor.Dispose();
            Require(source.Opens == 1 && source.Closes == 1 && !monitor.IsOpen, "Hardware leaked or reopened after close");
            Expect<ObjectDisposedException>(() => monitor.OpenAsync()); return new { opens = source.Opens, closes = source.Closes };
        });
        Check("sensors: immutable snapshot and invalid numeric data", () =>
        {
            using var monitor = new SensorMonitor(); var row = new SensorRow { Id = "test", Name = "Temperature", Hardware = "test", Type = SensorType.Temperature };
            row.Update(42, 40, 45); monitor.Rows.Add(row); var snapshot = monitor.Snapshot(); row.Update(99, 40, 99);
            Require(snapshot[0].RawValue == 42, "Snapshot shared mutable sensor state"); row.Update(float.NaN, float.PositiveInfinity, null);
            Require(row.RawValue == null && row.Value == "—", "Invalid float displayed as measurement"); return new { snapshotStable = true, invalidUnknown = true };
        });
        Check("driver downloads: official HTTPS and exact publisher only", () =>
        {
            Require(DriverDownloadPolicy.Validate("https://us.download.nvidia.com/driver.exe", "NVIDIA").Scheme == "https", "Official domain rejected");
            foreach (string url in new[] { "http://download.nvidia.com/x.exe", "https://nvidia.com.evil.test/x.exe", "https://evil.test/nvidia.com/x.exe", "https://user@download.nvidia.com/x.exe", "file:///C:/driver.exe", "https://download.nvidia.com:444/x.exe" })
                Expect<InvalidOperationException>(() => DriverDownloadPolicy.Validate(url, "NVIDIA"));
            Require(DriverDownloadPolicy.IsPublisher("NVIDIA Corporation", "NVIDIA"), "Legitimate publisher rejected");
            Require(!DriverDownloadPolicy.IsPublisher("Fake NVIDIA Corporation", "NVIDIA"), "Substring publisher accepted");
            Require(GpuDriverStatus.CompareVersions("610.10", "610.9") > 0, "Version components compared as decimals");
            return new { domainAndPublisherSpoofingRejected = true };
        });
        Check("Windows Update: individual errors cannot become total success", () =>
        {
            string text = WindowsUpdateService.Summarize(new[] { new WindowsUpdateService.Outcome("one", 2, 0), new WindowsUpdateService.Outcome("two", 4, unchecked((int)0x80004005)) }, true);
            Require(text.Contains("parcial") && text.Contains("1 de 2") && text.Contains("two") && text.Contains("Reinicie"), "Partial installation hidden");
            return new { partialReported = true };
        });
        Check("winget: exact package arguments reject option injection", () =>
        {
            var args = AppInstaller.BuildArguments("7zip.7zip", false);
            Require(args.Contains("--source") && args.Contains("winget") && args.Contains("--exact"), "Source/package not constrained");
            Expect<ArgumentException>(() => AppInstaller.BuildArguments("7zip.7zip --source evil", false)); return new { noShell = true };
        });
        Check("processes: both pipes drain, output is bounded and quoting survives", () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = RunFixture(new[] { "pipes" }, timeout.Token).GetAwaiter().GetResult();
            Require(result.ExitCode == 0 && result.Output.EndsWith("OUTEND") && result.Error.EndsWith("ERREND"), "Pipe drain failed");
            Require(result.Output.Length <= 65536 && result.Error.Length <= 65536, "Unbounded output");
            const string argument = "a b\" & --literal";
            var echo = RunFixture(new[] { "echo", argument }, timeout.Token).GetAwaiter().GetResult();
            Require(echo.Output == argument, "Arguments were interpreted as a command"); return new { bounded = true, literalArguments = true };
        });
        Check("processes: cancellation terminates the owned child", () =>
        {
            using var test = new CaseFolder(); using var cancel = new CancellationTokenSource(); string pidFile = Path.Combine(test.Path, "pid.txt");
            Task<ProcessResult> run = RunFixture(new[] { "sleep", pidFile }, cancel.Token);
            var watch = Stopwatch.StartNew(); while (!File.Exists(pidFile) && watch.Elapsed.TotalSeconds < 10 && !run.IsCompleted) Thread.Sleep(20);
            cancel.Cancel(); Expect<OperationCanceledException>(() => run.GetAwaiter().GetResult());
            Require(File.Exists(pidFile), "Child did not start"); int pid = int.Parse(File.ReadAllText(pidFile));
            bool alive; try { using var child = Process.GetProcessById(pid); alive = !child.HasExited; } catch (ArgumentException) { alive = false; }
            Require(!alive, "Cancelled child still running"); return new { ownedChildStopped = true };
        });
        Check("startup: backup write failure leaves the original untouched", () =>
        {
            using var test = new CaseFolder(); string original = Path.Combine(test.Path, "entry.txt"); File.WriteAllText(original, "original");
            string blocked = Path.Combine(test.Path, "blocked"); File.WriteAllText(blocked, "not a directory");
            var backend = new FileStartupMutations(original); var service = new StartupService(backend, new StartupBackupStore(blocked));
            Expect<IOException>(() => service.Remove(new StartupEntry()));
            Require(File.ReadAllText(original) == "original" && backend.Removals == 0, "Entry changed before durable backup");
            return new { persistBeforeMutation = true };
        });
        Check("startup: canonical restore ignores edited UI data and preserves backup after failure", () =>
        {
            using var test = new CaseFolder(); string original = Path.Combine(test.Path, "entry.txt"); File.WriteAllText(original, "original");
            var backend = new FileStartupMutations(original); var store = new StartupBackupStore(Path.Combine(test.Path, "backups"));
            var service = new StartupService(backend, store); service.Remove(new StartupEntry());
            var saved = service.Backups.Single(); Require(!File.Exists(original), "Remove did not run");
            backend.FailRestore = true; Expect<IOException>(() => service.Restore(saved)); Require(service.Backups.Count == 1, "Recovery record deleted after failure");
            backend.FailRestore = false; service.Restore(new StartupBackup { Id = saved.Id, Command = "tampered" });
            Require(File.ReadAllText(original) == "original" && service.Backups.Count == 0, "Untrusted UI data used for restore");
            Expect<InvalidOperationException>(() => store.Read("../outside")); return new { canonicalDataOnly = true, retryable = true };
        });
        Check("FPS API: invalid data rejected, valid cache reused and key changes isolated", () =>
        {
            using var test = new CaseFolder();
            var handler = new HttpFixture(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"avgFps\":120,\"min1Fps\":80}", Encoding.UTF8, "application/json") });
            using var http = new HttpClient(handler);
            var client = new HowManyFpsClient(http, Path.Combine(test.Path, "cache.json")) { ApiKey = "unit-test-value" };
            CoreBi.Engine.Games.HowManyFpsClient.Result Get() => client.GetAsync("cpu", "gpu", "game", "1080p", "native").GetAwaiter().GetResult();
            Require(Get().AvgFps == 120 && Get().Min1Fps == 80 && handler.Calls == 1, "Valid cache missed");
            Require(!File.ReadAllText(Path.Combine(test.Path, "cache.json")).Contains("unit-test-value"), "Credential stored in cache");
            client.ApiKey = "another-test-value"; Get(); Require(handler.Calls == 2, "Changed key reused another account cache");
            handler.Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"avgFps\":-1}", Encoding.UTF8, "application/json") };
            client.ApiKey = "invalid-response-test"; Expect<HttpRequestException>(() => Get());
            handler.Response = () => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://example.invalid/") } };
            Expect<HttpRequestException>(() => Get()); return new { invalidRejected = true, cacheScoped = true, redirectRejected = true, scope = "controlled HTTP fixture; external API not certified" };
        });
        Check("benchmarks: invalid sizes and pre-cancelled work do not create files", () =>
        {
            using var test = new CaseFolder(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Expect<ArgumentOutOfRangeException>(() => DiskBench.Run(test.Path, 0, (_, _) => { }, CancellationToken.None));
            Expect<OperationCanceledException>(() => DiskBench.Run(test.Path, 16, (_, _) => { }, cancel.Token));
            Expect<OperationCanceledException>(() => MemoryBench.MeasureLatencyNs(cancel.Token));
            Expect<OperationCanceledException>(() => MemoryBench.MeasureBandwidth(cancel.Token));
            Expect<OperationCanceledException>(() => MemoryBench.MeasureLoading((_, _) => { }, cancel.Token));
            Require(!Directory.EnumerateFileSystemEntries(test.Path).Any(), "Pre-cancelled workload created files"); return new { noPartialMeasurements = true };
        });
        Check("game references: unknown models produce no invented FPS or balance", () =>
        {
            Require(GameCatalog.GpuPerf("RTX 4060 Laptop GPU") == null, "Desktop index used for mobile GPU");
            Require(GameCatalog.CpuPerf("Ryzen 7 15700X") == null, "Partial model match");
            var estimate = GameCatalog.Estimate(GameCatalog.Games[0], 0, 80, "1920x1080", "Alto", "Native");
            Require(estimate.Avg == 0 && estimate.Limiter == Bottleneck.Unknown, "Unknown model produced numeric FPS");
            Require(BottleneckAnalyzer.Balance(0, 0).Text.Contains("indisponível"), "Unknown balance presented as balanced");
            return new { unknownPreserved = true };
        });
        Check("network: successful HTTP is not diagnosed as offline due to blocked ping", () =>
        {
            Require(CoreBi.Engine.Monitoring.Network.NetworkTest.HasHttpEvidence(100, null), "HTTP evidence ignored");
            Require(!CoreBi.Engine.Monitoring.Network.NetworkTest.HasHttpEvidence(null, null), "Missing HTTP treated as success");
            return new { icmpAndHttpDistinguished = true };
        });
        Check("network: lost pings count as loss and stay out of latency and jitter", () =>
        {
            var ping = new CoreBi.Engine.Monitoring.Network.PingResult("Roteador", "192.168.0.1", new double?[] { 10, null, 14, 12 });
            Require(ping.Sent == 4 && ping.Lost == 1 && ping.LossPct == 25, "Loss miscounted");
            Require(ping.Avg == 12 && ping.Min == 10 && ping.Max == 14 && ping.Jitter == 3, "Latency or jitter used a lost sample");
            var silent = new CoreBi.Engine.Monitoring.Network.PingResult("Internet", "1.1.1.1", new double?[] { null, null });
            Require(!silent.Answered && silent.Text == "não responde", "Silent target presented as answered");
            return new { lossPct = ping.LossPct, jitter = ping.Jitter };
        });
    }

    private sealed class CaseFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(Output, "case-" + Guid.NewGuid().ToString("N"));
        public CaseFolder() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
    private sealed class BlockingHardwareSource : IHardwareSource, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new(); public ManualResetEventSlim Release { get; } = new();
        public int Opens, Closes; public IEnumerable<IHardware> Hardware => Array.Empty<IHardware>();
        public void Open() { Interlocked.Increment(ref Opens); Entered.Set(); if (!Release.Wait(5000)) throw new TimeoutException(); }
        public void Close() => Interlocked.Increment(ref Closes);
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
    private sealed class FileStartupMutations(string file) : IStartupMutations
    {
        public int Removals; public bool FailRestore;
        public StartupBackup Prepare(StartupEntry entry, string folder) => new() { Command = File.ReadAllText(file) };
        public void Remove(StartupEntry entry, StartupBackup snapshot) { Removals++; File.Delete(file); }
        public void Restore(StartupBackup backup) { if (FailRestore) throw new IOException("Injected restore failure"); File.WriteAllText(file, backup.Command); }
    }
    private sealed class HttpFixture(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Response = response; public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return Task.FromResult(Response()); }
    }
    private static Task<ProcessResult> RunFixture(string[] arguments, CancellationToken token)
    {
        string host = System.IO.Path.GetFullPath(System.IO.Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        return ProcessRunner.RunAsync(host, new[] { Assembly.GetExecutingAssembly().Location, "--process-fixture" }.Concat(arguments), token);
    }
    private static int ProcessFixture(string[] args)
    {
        if (args[1] == "pipes") { Console.Out.Write(new string('x', 200000) + "OUTEND"); Console.Error.Write(new string('y', 200000) + "ERREND"); }
        else if (args[1] == "echo") Console.Write(args[2]);
        else if (args[1] == "sleep") { File.WriteAllText(args[2], Environment.ProcessId.ToString()); Thread.Sleep(30000); }
        return 0;
    }
}
