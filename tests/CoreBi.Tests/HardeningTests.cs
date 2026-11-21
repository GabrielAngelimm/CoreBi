using System.Diagnostics;
using System.IO;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Games;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Performance.Benchmarks;
using CoreBi.Engine.Tools.Apps;
using CoreBi.Engine.Tools.Drivers;
using CoreBi.Engine.Tools.Startup;
using CoreBi.Engine.Persistence;
using CoreBi.Desktop.Features.Monitoring.Sensors;
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
            using var folder = new CaseFolder();
            using var _ = AppPaths.OverrideDataFolder(folder.Path);
            Directory.CreateDirectory(AppSettings.Folder);
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
        Check("sensors: device change pauses reads and a driver swap retires only the GPU", () =>
        {
            using var monitor = new SensorMonitor();
            var gpu = new SensorRow { Id = "g", Name = "GPU Core", Hardware = "gpu", HardwareType = HardwareType.GpuNvidia, Type = SensorType.Temperature };
            var cpu = new SensorRow { Id = "c", Name = "Core Max", Hardware = "cpu", HardwareType = HardwareType.Cpu, Type = SensorType.Temperature };
            gpu.Update(60, 50, 70); cpu.Update(55, 40, 80); monitor.Rows.Add(gpu); monitor.Rows.Add(cpu);
            try
            {
                monitor.Pause(); Require(monitor.IsPaused && GpuAccessGate.IsSuspended, "Reads kept running during a device change");
                monitor.Resume(); Require(!monitor.IsPaused && !GpuAccessGate.IsSuspended, "Plain device change left reads paused");
                monitor.Pause(); monitor.RetireGpu(); monitor.Resume();
                Require(monitor.GpuRetired && GpuAccessGate.IsSuspended, "GPU came back after a driver swap");
                Require(gpu.RawValue == null && cpu.RawValue == 55, "Retiring the GPU touched other hardware or kept a stale reading");
            }
            finally { GpuAccessGate.SetSuspended(false); } // os testes seguintes ainda leem a placa
            return new { gpuRetired = true, cpuKept = true };
        });
        Check("sensors: session statistics, reset and clear names", () =>
        {
            var row = new SensorRow { Id = "t", Name = "Core #3", Hardware = "cpu", HardwareType = HardwareType.Cpu, Type = SensorType.Clock };
            row.Update(4000, 3000, 4500); row.Update(2000, null, null); row.Update(null, null, null); row.Update(3000, null, null);
            Require(row.RawMin == 2000 && row.RawMax == 4500 && row.AvgValue == 3000, "Min/avg/max not tracked across readings");
            Require(row.Label == "Núcleo 3" && row.Tip == "Core #3" && row.Unit == "MHz" && row.Value == "3000 MHz", "Clock row label or format");
            row.ResetStats();
            Require(row.RawMin == 3000 && row.RawMax == 3000 && row.AvgValue == 3000, "Reset did not restart statistics");
            var ssd = new SensorRow { Id = "s", Name = "Temperature", Hardware = "ssd", Type = SensorType.Temperature };
            ssd.Update(72, 0, 72);
            Require(ssd.RawMin == 72, "Zero minimum from the first library read shown for a temperature");
            Require(SensorNaming.Label(HardwareType.GpuNvidia, SensorType.Temperature, "GPU Hot Spot") == "Ponto mais quente", "GPU name");
            Require(SensorNaming.Label(HardwareType.Cpu, SensorType.Load, "CPU Core #2 Thread #1") == "Núcleo 2 · thread 1", "Thread name");
            Require(SensorNaming.Label(HardwareType.Cpu, SensorType.Power, "Package") == "Total do processador", "Typed name");
            Require(SensorNaming.Label(HardwareType.Storage, SensorType.Load, "Something New") == "Something New", "Unknown name changed");
            var hot = new SensorRow { Name = "Temperature", HardwareType = HardwareType.Storage, Type = SensorType.Temperature };
            hot.Update(64, null, null);
            var written = new SensorRow { Name = "Data Written", HardwareType = HardwareType.Storage, Type = SensorType.Data };
            written.Update(1200, null, null);
            Require(hot.Level == 1 && hot.Number == 64f.ToString("0.0"), "Disk temperature verdict or fixed decimals");
            Require(written.IsCounter && written.MaxNumber == "", "Counter shows statistics");
            Require(!PhysicalNetwork.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "Intel(R) Ethernet Controller I225-V-WFP Native MAC Layer LightWeight Filter-0000", "Ethernet-WFP")
                && !PhysicalNetwork.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211, "Microsoft Wi-Fi Direct Virtual Adapter", "Conexão Local* 8")
                && !PhysicalNetwork.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "Hyper-V Virtual Ethernet Adapter", "vEthernet (Default Switch)")
                && !PhysicalNetwork.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "Microsoft Kernel Debug Network Adapter", "Ethernet (Depurador de Kernel)")
                && PhysicalNetwork.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "Realtek Gaming 2.5GbE Family Controller", "Ethernet")
                && PhysicalNetwork.IsPhysical(System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211, "Intel(R) Wi-Fi 6 AX200 160MHz", "Wi-Fi"), "Physical network filter");
            return new { label = row.Label, avg = row.AvgNumber, diskLevel = hot.Level };
        });
        Check("sensors: component order, Windows memory left out and component filter", () =>
        {
            SensorRow R(string id, HardwareType hw, string hwName, SensorType type, string name, float value)
            {
                var row = new SensorRow { Id = id + name, Hardware = hwName, HardwareType = hw, RootId = id, RootName = hwName, RootType = hw, Name = name, Type = type };
                row.Update(value, null, null); return row;
            }
            var rows = new List<SensorRow>
            {
                R("/ram/total", HardwareType.Memory, "Total Memory", SensorType.Load, "Memory", 48), R("/ram/total", HardwareType.Memory, "Total Memory", SensorType.Data, "Memory Used", 15),
                R("/ram/total", HardwareType.Memory, "Total Memory", SensorType.Load, "Virtual Memory", 41), R("/ram/total", HardwareType.Memory, "Total Memory", SensorType.Data, "Virtual Memory Used", 19),
                R("/nic/1", HardwareType.Network, "Ethernet", SensorType.Load, "Network Utilization", 2),
                R("/ram/dimm/1", HardwareType.Memory, "Asgard B", SensorType.Temperature, "DIMM Temp", 41), R("/ram/dimm/0", HardwareType.Memory, "Asgard A", SensorType.Temperature, "DIMM Temp", 40),
                R("/mb", HardwareType.Motherboard, "TUF B550", SensorType.Temperature, "System", 36), R("/ssd", HardwareType.Storage, "Netac NV7000", SensorType.Temperature, "Temperature", 50),
                R("/gpu", HardwareType.GpuNvidia, "RTX 4060 Ti", SensorType.Load, "GPU Core", 47), R("/cpu", HardwareType.Cpu, "Ryzen 7", SensorType.Load, "CPU Total", 30), R("/cpu", HardwareType.Cpu, "Ryzen 7", SensorType.Fan, "Fan #1", 900),
            };
            var all = SensorBoard.Build(rows, null, null, "", new HashSet<string>());
            string[] order = all.Cards.Select(c => c.Name).ToArray();
            Require(string.Join("|", order) == "Ryzen 7|RTX 4060 Ti|Netac NV7000|TUF B550|Asgard A|Asgard B|Ethernet", "Component order: " + string.Join("|", order));
            Require(all.Cards[4].Kind == "Memória RAM · módulo 1 de 2" && all.Index[0].Id == null && all.Index.Count == 8, "Index or module label");
            Require(SensorBoard.Find(rows, "cpu") == "/cpu" && SensorBoard.Find(rows, "ram") == "/ram/dimm/0" && SensorBoard.Find(rows, "board") == "/mb" && SensorBoard.Find(rows, "x") == null, "Shortcut targets");
            var one = SensorBoard.Build(rows, "/ssd", null, "", new HashSet<string>());
            Require(one.Cards.Count == 1 && one.Cards[0].Name == "Netac NV7000" && !one.Cards[0].CanCollapse && one.Filters.Count == 1 && one.Index.Count == 8, "Component filter");
            var fans = SensorBoard.Build(rows, null, "fan", "", new HashSet<string>());
            Require(fans.Cards.Count == 1 && fans.Cards[0].Name == "Ryzen 7", "Type filter");
            var stale = SensorBoard.Build(rows, "/gone", "fan", "", new HashSet<string>());
            Require(stale.SelectedId == null && stale.Cards.Count == 1, "Missing component not reset");
            var split = new List<SensorRow>
            {
                R("/ram", HardwareType.Memory, "Total Memory", SensorType.Load, "Memory", 48), R("/ram", HardwareType.Memory, "Total Memory", SensorType.Data, "Memory Used", 15),
                R("/vram", HardwareType.Memory, "Virtual Memory", SensorType.Load, "Memory", 41), R("/vram", HardwareType.Memory, "Virtual Memory", SensorType.Data, "Memory Used", 19),
            };
            Require(string.Join("|", SensorBoard.Build(split, null, null, "", new HashSet<string>()).Cards.Select(c => c.Name)) == "", "Windows memory in two hardware blocks still shown");
            return new { order, filters = all.Filters.Count };
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
            var args = AppInstaller.BuildArguments("7zip.7zip");
            Require(args.Contains("--source") && args.Contains("winget") && args.Contains("--exact"), "Source/package not constrained");
            Expect<ArgumentException>(() => AppInstaller.BuildArguments("7zip.7zip --source evil")); return new { noShell = true };
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
        Check("benchmarks: invalid sizes and pre-cancelled work do not create files", () =>
        {
            using var test = new CaseFolder(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Expect<ArgumentOutOfRangeException>(() => DiskBench.Run(test.Path, 0, (_, _) => { }, CancellationToken.None));
            Expect<OperationCanceledException>(() => DiskBench.Run(test.Path, 16, (_, _) => { }, cancel.Token));
            Expect<OperationCanceledException>(() => MemoryBench.MeasureLatencyNs(cancel.Token));
            Expect<OperationCanceledException>(() => MemoryBench.MeasureBandwidth(cancel.Token));
            Expect<OperationCanceledException>(() => LoadingBench.Measure((_, _) => { }, cancel.Token));
            Require(!Directory.EnumerateFileSystemEntries(test.Path).Any(), "Pre-cancelled workload created files"); return new { noPartialMeasurements = true };
        });
        Check("hardware references: unknown models produce no invented balance", () =>
        {
            Require(HardwareCatalog.GpuPerf("RTX 4060 Laptop GPU") == null, "Desktop index used for mobile GPU");
            Require(HardwareCatalog.CpuPerf("Ryzen 7 15700X") == null, "Partial model match");
            Require(BottleneckAnalyzer.Balance(0, 0).Text.Contains("indisponível"), "Unknown balance presented as balanced");
            return new { unknownPreserved = true };
        });
        Check("network: successful HTTP is not diagnosed as offline due to blocked ping", () =>
        {
            var silentRouter = new CoreBi.Engine.Monitoring.Network.PingResult("Roteador", "192.168.0.1", new double?[] { null, null });
            var silentInternet = new CoreBi.Engine.Monitoring.Network.PingResult("Internet", "1.1.1.1", new double?[] { null, null });
            var withHttp = CoreBi.Engine.Monitoring.Network.NetworkDiagnostics.Analyze(
                silentRouter, new[] { silentInternet }, silentInternet,
                dnsMs: 12, downMbps: 250, upMbps: 80, downError: null, upError: null, upIncomplete: false,
                providerError: null, loadedMs: null, limitedUntil: null, rateLimited: false);
            var noHttp = CoreBi.Engine.Monitoring.Network.NetworkDiagnostics.Analyze(
                silentRouter, new[] { silentInternet }, silentInternet,
                dnsMs: null, downMbps: null, upMbps: null, downError: "timeout", upError: "timeout", upIncomplete: false,
                providerError: null, loadedMs: null, limitedUntil: null, rateLimited: false);
            Require(withHttp.Overall != CoreBi.Engine.Monitoring.Health.HealthVerdict.Critical
                && withHttp.InternetVerdict == CoreBi.Engine.Monitoring.Health.HealthVerdict.Unknown,
                $"HTTP evidence ignored by diagnostics: {withHttp.Overall}/{withHttp.InternetVerdict}");
            Require(noHttp.Overall == CoreBi.Engine.Monitoring.Health.HealthVerdict.Critical
                && noHttp.InternetVerdict == CoreBi.Engine.Monitoring.Health.HealthVerdict.Critical,
                $"Missing ICMP + HTTP not flagged Critical: {noHttp.Overall}/{noHttp.InternetVerdict}");
            return new { withHttp = withHttp.Overall.ToString(), noHttp = noHttp.Overall.ToString() };
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
    private static Task<ProcessResult> RunFixture(string[] arguments, CancellationToken token)
    {
        string processPath = Environment.ProcessPath!;
        if (processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessRunner.RunAsync(processPath, new[] { "--process-fixture" }.Concat(arguments), token);
        }
        return ProcessRunner.RunAsync(processPath, new[] { Path.Combine(AppContext.BaseDirectory, "CoreBi.Tests.dll"), "--process-fixture" }.Concat(arguments), token);
    }
    private static int ProcessFixture(string[] args)
    {
        if (args[1] == "pipes") { Console.Out.Write(new string('x', 200000) + "OUTEND"); Console.Error.Write(new string('y', 200000) + "ERREND"); }
        else if (args[1] == "echo") Console.Write(args[2]);
        else if (args[1] == "sleep")
        {
            string tmp = args[2] + ".tmp";
            File.WriteAllText(tmp, Environment.ProcessId.ToString());
            File.Move(tmp, args[2], overwrite: true);
            Thread.Sleep(30000);
        }
        return 0;
    }
}
