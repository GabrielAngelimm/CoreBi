using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CoreBi.Desktop;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Games;
using CoreBi.Engine.Performance.Benchmarks;
using CoreBi.Engine.Performance.Stress;
using CoreBi.Engine.Monitoring.Power;
using CoreBi.Engine.Tools.Startup;
using CoreBi.Engine.Tools.Drivers;
using CoreBi.Engine.Tools.Apps;
using CoreBi.Engine.Persistence;
using LibreHardwareMonitor.Hardware;

internal static partial class Program
{
    private sealed class TestApplication : Application
    {
        public TestApplication()
        {
            // Use production resources without claiming the app's mutex or creating a foreground window.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            foreach (string resource in new[] { "Theme.xaml", "FeatureTemplates.xaml" })
                Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/CoreBi.Desktop;component/Shell/{resource}", UriKind.Relative) });
        }
    }
    private static readonly List<object> Results = new();
    private static string Output = "";
    private static int Failures;
    private static void Check(string name, Func<object> test)
    {
        var watch = Stopwatch.StartNew();
        try { var details = test(); Results.Add(new { name, status = "PASS", elapsedMs = watch.ElapsedMilliseconds, details }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Failures++; Results.Add(new { name, status = "FAIL", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--process-fixture") return ProcessFixture(args);
        Output = Path.GetFullPath(args.Length > 0 ? args[0] : "TestResults");
        Directory.CreateDirectory(Output);
        Environment.SetEnvironmentVariable("COREBI_DATA_DIR", Path.Combine(Output, "settings"));
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        if (args.Contains("--overlay-telemetry")) OverlayTelemetry();
        else if (args.Contains("--bench")) Benches(); else if (args.Contains("--hardware")) Hardware(); else if (args.Contains("--startup")) Startup(); else if (args.Contains("--integrations")) Integrations(); else { Logic(); TelemetryTests(); RtssTests(); Hardening(); Ui(); }
        var text = JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(Output, args.Contains("--bench") ? "bench.json" : args.Contains("--hardware") ? "hardware.json" : args.Contains("--startup") ? "startup.json" : args.Contains("--integrations") ? "integrations.json" : "tests.json"), text);
        return Failures == 0 ? 0 : 1;
    }
    private static void Logic()
    {
        Check("workloads: exclusion and idempotent release", () =>
        {
            var gate = new WorkloadCoordinator(); var first = gate.TryBegin("render"); Require(first != null, "First owner");
            Require(gate.TryBegin("stress") == null, "Concurrent stress must be rejected");
            first!.Dispose(); var second = gate.TryBegin("stress"); first.Dispose(); Require(gate.IsBusy, "Old lease must not release a new owner"); second!.Dispose();
            Require(!gate.IsBusy, "Release owner"); return new { rejectedConcurrent = true, staleDisposeSafe = true };
        });
        Check("calendar dates: BIOS and driver keep their calendar day", () =>
        {
            Require(WmiCalendarDate.Format("20250926000000.000000+000") == "26/09/2025", "BIOS date shifted");
            Require(WmiCalendarDate.Format("20260722000000.000000+000") == "22/07/2026", "Driver date shifted");
            Require(WmiCalendarDate.Format(null) == "—", "Missing date"); return new { bios = "26/09/2025", driver = "22/07/2026" };
        });
        Check("inventory: RAM slots, channels, XMP and readable values", () =>
        {
            RamModule Module(string loc, string? ch, int? idx, int conf) => new("BANK 0", loc, ch != null ? $"{ch}{idx}" : loc, ch, idx, 16UL << 30, "DDR4", "DIMM",
                2133, conf, "G.Skill", "F4-3600C16-16GVKC", "0", 64, 64, 2, 1.35, 1.2, 1.35);
            var ram = new RamInfo { Modules = { Module("DIMM_A2", "A", 2, 3600), Module("DIMM_B2", "B", 2, 3600) }, Slots = 4, PartSpeed = 3600 };
            Require(string.Join(",", ram.SlotMap().Select(s => s.Name + (s.Module != null ? "*" : ""))) == "A1,A2*,B1,B2*", "Slot map");
            Require(ram.Channels == 2 && ram.Profile!.Tone == Tone.Ok && ram.ChannelFinding!.Tone == Tone.Ok, "Dual channel with XMP");
            var off = new RamInfo { Modules = { Module("DIMM_A2", "A", 2, 2133), Module("DIMM_A1", "A", 1, 2133) }, Slots = 4, PartSpeed = 3600 };
            Require(off.Profile!.Tone == Tone.Warn && off.ChannelFinding!.Tone == Tone.Warn, "XMP off and single channel must warn");
            var odd = new RamInfo { Modules = { Module("XMM1", null, null, 3200) }, Slots = 2 };
            Require(odd.SlotMap().Count == 2 && odd.SlotMap()[1].Module == null, "Unknown slot names keep empty slots");
            Require(SystemInfoCollector.NvidiaVersion("32.0.15.6636") == "566.36" && SystemInfoCollector.NvidiaVersion("31.0.15.5222") == "552.22", "NVIDIA version");
            Require(Fmt.IsPlaceholder("Default string") && Fmt.ShortMaker("ASUSTeK COMPUTER INC.") == "ASUS" && Fmt.Bytes(16UL << 30) == "16 GB", "Readable values");
            Require(Fmt.Ago(new DateTime(2024, 3, 15), new DateTime(2025, 10, 20)) == "há 1 ano e 7 meses", "Age");

            var inv = new SystemInventory { Ram = ram };
            inv.Cpus.Add(new CpuInfo("AMD Ryzen 7 5700X 8-Core Processor", "AMD", "AM4", 8, 16, 3400, 25, 33, 2, "x64", true, false, 4096, 32768,
                new CpuTopology(), new() { ("AVX2", true), ("AVX-512", false) }));
            inv.Gpus.Add(new VideoAdapter("NVIDIA GeForce RTX 4060 Ti", "NVIDIA", "566.36", "32.0.15.6636", null, 8UL << 30));
            inv.Displays.Add(new DisplayInfo(1, "NVIDIA GeForce RTX 4060 Ti", "LG ULTRAGEAR", "GSM", 27, 1920, 1080, 60, 144, 32, 0, 0, true, "DisplayPort", 2023, 22));
            inv.Disks.Add(new PhysicalDiskInfo(0, "Samsung SSD 980 1TB", 1UL << 40, "NVMe", "SSD", "3B4QFXO7", "S64ANS0T", false, "Saudável", "GPT"));
            inv.Settings = new WindowsSettings(false, true, true, true, "(UTC-03:00) Brasília");
            inv.Os = new OsInfo("Windows 11 Pro", "Windows 11 Pro", "24H2", "26100.6899", "64 bits (x64)", null, null, "PC", "user", "WORKGROUP", false, "português (Brasil)", null, null);
            var (index, sheets) = CoreBi.Desktop.Features.Monitoring.Inventory.SystemSheets.Build(inv, Array.Empty<CoreBi.Engine.Monitoring.Graphics.GpuInfo>(), Array.Empty<CoreBi.Engine.Monitoring.Storage.DriveCard>(), DateTime.Now);
            Require(index.Count == 6 && sheets.Count == 6, "Six sheets");
            Require(index[0].Name == "Ryzen 7 5700X" && index[0].Spec.StartsWith("8 núcleos · 16 threads"), "CPU index: " + index[0].Spec);
            var ramRows = sheets["ram"].Groups.SelectMany(g => g.Rows).ToList();
            Require(ramRows.Any(r => r.Key == "Canais" && r.Value == "Dual channel" && r.Tone == Tone.Ok) && ramRows.Any(r => r.Key == "Perfil XMP/EXPO" && r.Tone == Tone.Ok), "RAM verdicts live in the rows");
            Require(sheets["ram"].Groups.Any(g => g.Columns?.Count == 2 && g.Rows.All(r => r.Cells?.Count == 2)), "Two RAM modules side by side");
            Require(sheets["cpu"].Readouts.Count == 4 && index.All(i => i.Glyph.Length == 1 && i.Glyph[0] >= '\uE000'), "Index glyphs are icon characters");
            Require(sheets["gpu"].Groups.SelectMany(g => g.Rows).Any(r => r.Key == "Atualização" && r.Tone == Tone.Warn), "Display below max Hz warns in the row and the index");
            Require(sheets["storage"].Groups.Any(g => g.Rows.Any(r => r.Key == "Nº de série" && r.Value == "S64ANS0T")) && sheets["storage"].Disks![0].Meta.Length < 30, "Disk serial in its own row, not a truncated meta line");
            Require(sheets["os"].Groups.SelectMany(g => g.Rows).Any(r => r.Key == "Modo de jogo" && r.Tone == Tone.Warn), "Game mode off warns");
            Require(sheets["ram"].Groups.SelectMany(g => g.Rows).Any(r => r.Key == "Fabricante" && r.Cells![0] == "G.Skill"), "RAM maker");
            var allRows = sheets.Values.SelectMany(sh => sh.Groups).SelectMany(g => g.Rows).ToList();
            Require(allRows.All(r => r.Cells != null || r.Value != "—"), "Unreadable fields are hidden, not shown as a dash");
            Require(allRows.All(r => !(r.Value + r.Tip + r.Help).Contains("CPU-Z")), "No references to other programs");
            Require(sheets["gpu"].Groups.SelectMany(g => g.Rows).Any(r => r.Key == "Fabricado em" && r.Value == "Maio de 2023"), "Monitor week becomes month and year");
            Require(sheets["ram"].Groups.All(g => !g.Title.StartsWith("Uso")), "System page shows hardware, not live usage");
            Require(inv.ToNodes().Count == 6, "Report keeps six categories");
            return new { slots = "A1,A2*,B1,B2*", index = index.Select(i => i.Name).ToArray() };
        });
        Check("bottleneck: missing data cannot be a positive verdict", () =>
        {
            var missing = new LoadSample(null, null, null, null, null, null, null, null, false, null);
            Require(missing.Classify() == Limiter.Unknown, "Missing telemetry classified as capped");
            var thermal = new LoadSample(20, 20, 30, "1", 20, 30, 100, 40, false, 60); Require(thermal.Classify() == Limiter.Thermal, "GPU temperature ignored");
            var session = new BottleneckSession { Duration = TimeSpan.FromMinutes(1) }; session.Add(missing); Require(session.Count == 0, "Session counted missing data");
            return new { missing = missing.Classify().ToString(), thermal = thermal.Classify().ToString() };
        });
        Check("FPS: two swapchains must not double the FPS", () =>
        {
            using var frames = new FrameMonitor();
            for (int i = 0; i < 180; i++) { frames.RecordPresent(999999, 1, i * 1000.0 / 60); frames.RecordPresent(999999, 2, i * 1000.0 / 60 + 0.1); }
            var stats = frames.GetStats(999999); Require(stats != null && Math.Abs(stats.Fps - 60) < 0.01, "Double-counted presents");
            return new { stats!.Fps, stats.Low1Fps, scope = "synthetic present events; no game capture certification" };
        });
        Check("disk health: temperature alone is insufficient", () =>
        {
            var rawType = typeof(DiskHealth).GetNestedType("Raw", BindingFlags.NonPublic)!; var raw = Activator.CreateInstance(rawType)!;
            rawType.GetField("Temp")!.SetValue(raw, 35d);
            var card = (DiskHealthCard)typeof(DiskHealth).GetMethod("ToCard", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { raw })!;
            Require(card.Verdict == DiskVerdict.Unknown, "Temperature-only disk considered healthy"); return new { card.VerdictText };
        });
        Check("voltages: rails need a plausible reading and keep ATX order", () =>
        {
            var map = VoltageMapper.Map(new[]
            {
                new VoltageInput(HardwareType.SuperIO, "+3.3V", 3.344f, 3.328f, 3.36f),
                new VoltageInput(HardwareType.SuperIO, "+12V", 12.096f, 12.0f, 12.2f),
                new VoltageInput(HardwareType.SuperIO, "+5V", 1.02f, 1.0f, 1.1f), // canal sem calibração: não é trilho
                new VoltageInput(HardwareType.SuperIO, "+3V Standby", 3.39f, null, null),
                new VoltageInput(HardwareType.SuperIO, "CMOS Battery", 2.71f, null, null),
                new VoltageInput(HardwareType.Cpu, "Core #1 VID", 1.2f, 0.9f, 1.3f),
                new VoltageInput(HardwareType.Cpu, "Core (SVI2 TFN)", 1.288f, 0.93f, 1.39f),
                new VoltageInput(HardwareType.Cpu, "SoC (SVI2 TFN)", 1.094f, null, null),
                new VoltageInput(HardwareType.GpuNvidia, "GPU Core", null, null, null),
            });
            Require(string.Join("|", map.Rails.Select(r => r.Title)) == "+12 V|+3,3 V|+3,3 V standby", "Rails: " + string.Join("|", map.Rails.Select(r => r.Title)));
            Require(map.Components.Select(c => c.Title).SequenceEqual(new[] { "Processador · núcleo", "Processador · SoC", "Bateria do CMOS" }), "Components: " + string.Join("|", map.Components.Select(c => c.Title)));
            Require(map.Components.Single(c => c.Title == "Bateria do CMOS").Low && map.Total == 8, "CMOS low or total");
            Require(map.Components[0].Value is > 1.28 and < 1.29, "SVI2 telemetry must win over VID");
            return new { rails = map.Rails.Count, components = map.Components.Count };
        });
        Check("power history: days count only what the log covers", () =>
        {
            var day = DateTime.Today.AddDays(-2);
            var h = new PowerHistory(DateTime.Today.AddDays(-6), day, new[] { new OnSpan(day.AddHours(22), day.AddHours(26), false) }, Array.Empty<DateTime>(), null);
            Require(h.OnTime(day) == TimeSpan.FromHours(2) && h.OnTime(day.AddDays(1)) == TimeSpan.FromHours(2), "Span across midnight");
            Require(h.Covers(day) && !h.Covers(day.AddDays(-1)), "Coverage");
            return new { split = "22:00–02:00 counts 2 h on each day" };
        });
        Check("RT: zero-frame passes cannot be reliable", () =>
        {
            var result = new GpuRtResult(new[] { new RtPass(0, 0, 0), new RtPass(0, 0, 0), new RtPass(0, 0, 0) }, 320, 180, "normal", "test", 0);
            Require(!result.Reliable, "Zero-frame result approved"); return new { result.Reliable };
        });
        Check("settings: separate storage, atomic save and protected API key", () =>
        {
            var settings = new AppSettings { LightTheme = true, HowManyFpsKey = "local-test-key" }; settings.Save();
            var loaded = AppSettings.Load(); Require(loaded.LightTheme && loaded.HowManyFpsKey == "local-test-key", "Settings roundtrip");
            var path = Path.Combine(AppSettings.Folder, "settings.json"); Require(!File.ReadAllText(path).Contains("local-test-key"), "Plaintext API key");
            Require(!Directory.GetFiles(AppSettings.Folder, "*.tmp").Any(), "Temporary settings left"); return new { isolated = true, encrypted = true };
        });
        Check("CPU render: deterministic samples and cancellation", () =>
        {
            var a = new BenchRenderer(); var b = new BenchRenderer();
            var render = typeof(BenchRenderer).GetMethod("RenderBucket", BindingFlags.Instance | BindingFlags.NonPublic)!;
            render.Invoke(a, new object[] { (440, 240, 8, 8), CancellationToken.None });
            render.Invoke(b, new object[] { (440, 240, 8, 8), CancellationToken.None });
            Require(a.Pixels.SequenceEqual(b.Pixels) && a.SamplesDone == 65536, "Render is not reproducible");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            var result = new BenchRenderer().Run(1, TimeSpan.Zero, cancel.Token);
            Require(result.Samples == 0 && result.Passes == 0, "Cancelled render ran");
            return new { samples = a.SamplesDone, scope = "8x8-pixel real render block; full preset not timed" };
        });
    }
    private static void Ui()
    {
        Check("WPF: all 15 selected pages load and navigate", () =>
        {
            var app = new TestApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var window = new MainWindow();
            string[] pages = { "PageDash", "PageSystem", "PageSensors", "PageGpu", "PageDisks", "PageEnergy", "PageNet", "PageGames", "PageBottleneck", "PageOverlay", "PageBench", "PageStress", "PageStartup", "PageDriver", "PageApps" };
            foreach (string page in pages) { window.ShowPage(page); Require(((FrameworkElement)window.FindName(page)).Visibility == Visibility.Visible, "Navigation failed: " + page); }
            foreach (string page in new[] { "PageOc", "PageOptimize", "PageInteg", "PageLauncher", "PageMakerDrv", "PageMakerPorts" }) Require(window.FindName(page) == null, "Removed page remained: " + page);
            using (var held = window._workloads.TryBegin("test"))
            {
                window.StartStress(); Require(window.StressHint.Text.Contains("benchmark"), "Stress ignored coordinator");
                window.PageNet.RunOrCancelAsync().GetAwaiter().GetResult();
                Require(window.PageNet.NetStatus.Text.Contains("Termine") && window.PageNet.LastResult == null, "Network ignored coordinator");
            }
            window.NavBench.IsChecked = true; Require(window.PageBench.Visibility == Visibility.Visible, "Sidebar event did not navigate");
            Require(window.PageBench.Owner == window, "Page not connected to shell");
            Require(window.Title == "CoreBi", "Original title remained");
            ThemeManager.Apply(true); ThemeManager.Apply(false);
            var overlay = new OverlayWindow(); overlay.Close();
            string diskStatus = window.PageDisks.DiskHealthStatus.Text, energyStatus = window.PageEnergy.EnergyStatus.Text;
            window.Close();
            Task.WhenAll(window.PageDisks.RefreshAsync(), window.PageEnergy.RefreshAsync(), window.PageNet.RunOrCancelAsync()).GetAwaiter().GetResult();
            Require(window.PageDisks.DiskHealthStatus.Text == diskStatus && window.PageEnergy.EnergyStatus.Text == energyStatus,
                "Closed monitoring pages accepted a new collection");
            return new { pages = pages.Length, removed = 6, sharedSensors = true, themeRoundTrip = true, stressBlockedDuringPreparation = true };
        });
        OverlayRenderingTests();
    }
    private static void Startup()
    {
        Check("WPF startup: real initialization and timer ticks without foreground activation", () =>
        {
            var app = new TestApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var window = new MainWindow();
            try
            {
                PumpDispatcher(window.LoadHardwareAsync());
                PumpDispatcher(Task.WhenAll(window.PageDisks.RefreshAsync(), window.PageEnergy.RefreshAsync()));
                Require(window.PageDisks.DiskCards.Items.Count > 0 && window.PageEnergy.Snapshot != null,
                    $"Monitoring pages: disks={window.PageDisks.DiskCards.Items.Count} ({window.PageDisks.DiskHealthStatus.Text}); power={window.PageEnergy.Snapshot != null} ({window.PageEnergy.EnergyStatus.Text})");
                var sampleFrame = new DispatcherFrame();
                var sampleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                sampleTimer.Tick += (_, _) => sampleFrame.Continue = false;
                sampleTimer.Start(); Dispatcher.PushFrame(sampleFrame); sampleTimer.Stop();
                Require(window._monitor.IsOpen && window._monitor.Rows.Count > 0, "Sensor collector not ready");
                Require(window._gpus.Count > 0 && window.PageSystem.CategoryCount == 6, "Startup did not bind inventory");
                return new
                {
                    sensorRows = window._monitor.Rows.Count,
                    gpus = window._gpus.Count,
                    diskCards = window.PageDisks.DiskCards.Items.Count,
                    powerPlans = window.PageEnergy.Plan?.Plans.Count ?? 0,
                    powerDays = window.PageEnergy.History?.Spans.Count ?? 0,
                    window.StatusText.Text,
                    elevated = SensorMonitor.IsElevated,
                    pawnIO = SensorMonitor.IsPawnIOInstalled,
                    etwIdle = !window._frames.Running,
                    scope = "real WPF dispatcher and hardware load; no foreground window or installation"
                };
            }
            finally { window.Close(); }
        });
    }
    private static void PumpDispatcher(Task task)
    {
        var frame = new DispatcherFrame(); var watch = Stopwatch.StartNew();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        poll.Tick += (_, _) => { if (task.IsCompleted || watch.Elapsed.TotalSeconds > 40) frame.Continue = false; };
        poll.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { poll.Stop(); }
        Require(task.IsCompleted, "WPF background collection exceeded 40 seconds");
        task.GetAwaiter().GetResult();
    }
    private static void Hardware()
    {
        if (SensorMonitor.IsElevated)
            Check("EventLog: real read-only hardware diagnostic query", () =>
            {
                var errors = CoreBi.Engine.Monitoring.Diagnostics.HardwareEventReader.ErrorsSince(DateTime.Now.AddDays(-1));
                Require(errors.Error == null, errors.Error ?? "EventLog query failed");
                return new { errors.Whea, errors.GpuResets, scope = "read-only System log; no events cleared or written" };
            });
        if (SensorMonitor.IsElevated)
            Check("ETW: real session start and stop", () =>
            {
                using var frames = new FrameMonitor();
                frames.Start();
                frames.Start();
                try { Require(frames.Running && frames.Error == null, frames.Error ?? "ETW did not start"); }
                finally { frames.Stop(); }
                frames.Stop();
                Require(!frames.Running, "ETW did not stop");
                return new { started = true, stopped = true, scope = "session lifecycle only; no game FPS calibration" };
            });
        Check("inventory: real system and GPU", () =>
        {
            var info = SystemInfoCollector.Collect(); var gpu = GpuInfoCollector.Collect();
            Require(info.Count == 6, "Inventory categories missing"); Require(gpu.Count > 0, "No GPU");
            return new { categories = info.Count, gpu = gpu.Select(x => x.Name).ToArray() };
        });
        Check("power and tools: read-only inventory", () =>
        {
            var power = BatteryInfo.Read(); var startup = StartupManager.List(); var drivers = DriverManager.Inventory();
            var apps = AppInstaller.Catalog(); AppInstaller.RefreshInstalled(apps);
            Require(power.Count > 0 && drivers.Count > 0 && apps.Count > 0, "Read-only inventories missing");
            return new { powerCards = power.Count, startupEntries = startup.Count, drivers = drivers.Count, apps = apps.Select(a => new { a.Name, a.Installed }).ToArray(), AppInstaller.WingetAvailable, scope = "no startup, driver or application modifications" };
        });
        Check("sensors and storage: real read with explicit CPU availability", () =>
        {
            using var monitor = new SensorMonitor(); monitor.OpenAsync().GetAwaiter().GetResult(); monitor.Refresh();
            Thread.Sleep(1100); monitor.Refresh();
            var disks = DiskHealth.Read(monitor.Snapshot()); Require(monitor.Rows.Count > 0 && disks.Count > 0, "No sensors/storage");
            return new { count = monitor.Rows.Count, withValue = monitor.Rows.Count(s => s.RawValue != null), cpu = monitor.DiagnoseCpuSensors().ToString(), monitor.CpuTemperature, monitor.CpuPower, disks = disks.Select(d => new { d.Title, d.VerdictText, d.Metrics }).ToArray() };
        });
    }
    private static void Benches()
    {
        Check("disk IO: unique temporary directory and worker-owned handles", () =>
        {
            var disk = DiskBench.Run(Output, 64, (_, _) => { }, CancellationToken.None);
            Require(disk.SeqReadMBs > 0 && disk.SeqWriteMBs > 0 && disk.ReadIopsQ8 > 0, "Disk IO failed");
            Require(!Directory.GetDirectories(Output, "CoreBiBench-*").Any(), "Disk temporary directory left behind");
            return new { disk, scope = "64 MiB functional test; not a calibrated preset" };
        });
        Check("CPU simulation: short real workload", () =>
        {
            var simulation = new GameSimBench(); var result = simulation.Run(TimeSpan.FromSeconds(1), CancellationToken.None);
            Require(result.Frames > 0 && result.AvgFps > 0, "No simulation frames");
            return new { result, scope = "short functional execution" };
        });
        Check("RAM: latency and bandwidth", () =>
        {
            var latency = MemoryBench.MeasureLatencyNs(CancellationToken.None);
            var bw = MemoryBench.MeasureBandwidth(CancellationToken.None);
            Require(double.IsFinite(latency) && latency > 0 && bw.Read > 0 && bw.Copy > 0, "Invalid memory result");
            return new { latencyNs = latency, readGBs = bw.Read, copyGBs = bw.Copy, scope = "single run; no external calibration" };
        });
        Check("loading: real files and cleanup", () =>
        {
            var result = MemoryBench.MeasureLoading((_, _) => { }, CancellationToken.None);
            Require(result.SeqReadMBs > 0 && result.FilesPerSec > 0, "No loading result");
            return new { result, scope = "functional loading workload; not actual game load time" };
        });
        string adapter = GpuInfoCollector.Collect().FirstOrDefault()?.Name ?? "";
        Check("GPU raster: real D3D11 frames", () =>
        {
            var bench = new GpuGameBench { Width = 640, Height = 360, Duration = TimeSpan.FromSeconds(3), AdapterName = adapter };
            var result = bench.Run(); Require(result is { Frames: > 0, AvgFps: > 0 }, bench.Error ?? "No raster frames");
            return new { result, scope = "640x360; 3 seconds; functional test" };
        });
        Check("GPU RT: real DXR frames", () =>
        {
            var bench = new GpuRtBench { Width = 320, Height = 180, Preset = "normal", Warmup = TimeSpan.FromSeconds(0.5), PassDuration = TimeSpan.FromSeconds(1), Passes = 3, AdapterName = adapter };
            var result = bench.Run(); Require(result != null && result.Passes.Count == 3 && result.Passes.All(p => p.Frames > 0), bench.Error ?? "No RT frames");
            return new { result, scope = "320x180; three 1-second passes; not a calibrated preset" };
        });
        Check("stress GPU: light start and stop", () =>
        {
            var burn = new GpuBurn(); var watch = Stopwatch.StartNew();
            try
            {
                burn.Start(new GpuBurnSettings { Width = 640, Height = 360, Shells = 16, Iterations = 4, Msaa = 1, AdapterName = adapter });
                while (burn.Running && burn.Frames < 30 && watch.Elapsed.TotalSeconds < 4) Thread.Sleep(30);
            }
            finally { burn.Stop(); }
            Require(burn.Error == null && burn.Frames > 0 && !burn.Running, burn.Error ?? "GPU did not stop");
            return new { burn.Frames, stopped = !burn.Running, scope = "brief light load; no thermal certification" };
        });
        Check("stress CPU: short start and stop", () =>
        {
            var stress = new StressTester();
            try { stress.Start(cpu: true, ram: false); Require(stress.Running, "CPU stress did not start"); Thread.Sleep(1000); }
            finally { stress.Stop(); }
            Require(!stress.Running, "CPU stress did not stop"); return new { stopped = true, scope = "one second; no thermal stability certification" };
        });
    }
}
