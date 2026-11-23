using System.IO;
using CoreBi.Desktop;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Health;
using CoreBi.Engine.Monitoring.Network;
using CoreBi.Engine.Monitoring.Power;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Performance.Benchmarks;
using CoreBi.Engine.Performance.Stress;
using CoreBi.Engine.Persistence;
using CoreBi.Engine.Tools.Apps;

/// <summary>
/// As regras que viram veredito, nota ou histórico na tela (1% low, notas, saúde do disco e da bateria, rede, Gargalo,
/// limites do histórico). Lógica pura: sem hardware, rede nem registro.
/// </summary>
internal static partial class Program
{
    private static void RuleTests()
    {
        Check("rules: 1% low is the 99th percentile frame time, median of odd and even counts", () =>
        {
            var ft = Enumerable.Repeat(10.0, 99).Append(50.0).ToArray();
            Require(FrameTimeStats.Percentile99(ft) == 50 && FrameTimeStats.Low1Fps(ft) == 20, "One slow frame in 100 is the 1% low");
            var ramp = Enumerable.Range(1, 200).Select(i => (double)i).ToArray();
            Require(FrameTimeStats.Percentile99(ramp) == 199, "Percentile index moved (stored scores would shift)");
            Require(FrameTimeStats.Percentile99(Array.Empty<double>()) == 0 && FrameTimeStats.Low1Fps(Array.Empty<double>()) == 0, "Empty run");
            Require(FrameTimeStats.Median(new[] { 3.0, 1, 2 }) == 2 && FrameTimeStats.Median(new[] { 1.0, 2, 3, 4 }) == 2.5, "Median");
            return new { low1 = FrameTimeStats.Low1Fps(ft) };
        });

        Check("rules: memory score formula", () =>
        {
            double score = MemoryBench.Score(80, 0.2);
            Require(Math.Abs(score - 62.5) < 1e-9, $"Memory score changed: {score}");
            Require(double.IsFinite(MemoryBench.Score(60, 0.05)) && double.IsFinite(MemoryBench.Score(120, 2)), "Plausible inputs gave a non-finite score");
            return new { score };
        });

        Check("rules: unsafe shutdowns are rated by count and by rate", () =>
        {
            var cases = new (ulong Count, long? Hours, HealthVerdict Expected)[]
            {
                (49, 100, HealthVerdict.Good),      // abaixo do mínimo para avisar
                (60, 1000, HealthVerdict.Warning),  // 0,06 por hora
                (60, 10000, HealthVerdict.Good),    // 0,006 por hora: disco antigo, normal
                (250, 1000, HealthVerdict.Critical),
                (250, 5000, HealthVerdict.Warning),
                (500, null, HealthVerdict.Good),    // sem horas ligadas não há taxa
                (500, 0, HealthVerdict.Good),
            };
            foreach (var (count, hours, expected) in cases)
                Require(DiskHealth.RateUnsafeShutdowns(count, hours) == expected, $"{count} desligamentos em {hours?.ToString() ?? "?"} h: esperado {expected}");
            return new { cases = cases.Length };
        });

        Check("rules: network verdict thresholds for loss, latency, jitter and a silent router", () =>
        {
            var router = new PingResult("Roteador", "192.168.0.1", new double?[] { 2, 2, 3, 2 });
            static PingResult Net(params double?[] samples) => new("Internet", "1.1.1.1", samples);
            static double?[] Steady(double ms, int count, int lost = 0) =>
                Enumerable.Repeat<double?>(ms, count - lost).Concat(Enumerable.Repeat<double?>(null, lost)).ToArray();
            NetworkDiagnostics.Summary Run(PingResult routerPing, PingResult internet) => NetworkDiagnostics.Analyze(
                routerPing, new[] { internet }, internet, dnsMs: 12, downMbps: 250, upMbps: 80, downError: null, upError: null,
                upIncomplete: false, providerError: null, loadedMs: null, limitedUntil: null, rateLimited: false);

            Require(Run(router, Net(Steady(20, 50))).InternetVerdict == HealthVerdict.Good, "Clean internet not good");
            Require(Run(router, Net(Steady(20, 50, lost: 1))).InternetVerdict == HealthVerdict.Warning, "2% loss not a warning");
            Require(Run(router, Net(Steady(20, 50, lost: 3))).InternetVerdict == HealthVerdict.Critical, "6% loss not critical");
            Require(Run(router, Net(Steady(90, 10))).InternetVerdict == HealthVerdict.Warning, "90 ms not a warning");
            Require(Run(router, Net(Steady(160, 10))).InternetVerdict == HealthVerdict.Critical, "160 ms not critical");
            var jittery = Net(10, 40, 10, 40);
            Require(jittery.Jitter > NetworkThresholds.InternetJitterWarnMs && Run(router, jittery).InternetVerdict == HealthVerdict.Warning, "High jitter not a warning");

            var silentRouter = new PingResult("Roteador", "192.168.0.1", new double?[] { null, null });
            var silent = Run(silentRouter, Net(Steady(20, 50)));
            Require(silent.Overall == HealthVerdict.Good && silent.LocalVerdict == HealthVerdict.Unknown, $"A router that blocks ping changed the verdict: {silent.Overall}");
            Require(silent.Notes.Any(n => n.StartsWith(NetworkThresholds.RouterSilentNotePrefix, StringComparison.Ordinal)), "Silent router note missing");
            return new { thresholds = new[] { NetworkThresholds.InternetLatencyWarnMs, NetworkThresholds.InternetLatencyCritMs } };
        });

        Check("rules: CPU and GPU balance bands, including the edges", () =>
        {
            string Band(double gpu) => BottleneckAnalyzer.Balance(gpu, 100).Text;
            Require(Band(190).StartsWith("GPU muito mais forte", StringComparison.Ordinal), "1.9");
            Require(Band(180).StartsWith("GPU um pouco à frente", StringComparison.Ordinal), "1.8 is not above 1.8");
            Require(Band(150).StartsWith("GPU um pouco à frente", StringComparison.Ordinal), "1.5");
            Require(Band(100).StartsWith("Hardware equilibrado", StringComparison.Ordinal), "1.0");
            Require(Band(70).StartsWith("Hardware equilibrado", StringComparison.Ordinal), "0.7 is balanced");
            Require(Band(50).StartsWith("CPU mais forte", StringComparison.Ordinal), "0.5");
            Require(Band(30).StartsWith("GPU muito fraca", StringComparison.Ordinal), "0.3");
            return new { ratio = BottleneckAnalyzer.Balance(150, 100).Ratio };
        });

        Check("rules: history caps keep the newest entries, newest first", () =>
        {
            var start = new DateTime(2026, 1, 1);
            var bottleneck = Enumerable.Range(0, 25)
                .Select(i => new BottleneckRun { Date = start.AddDays(i), Samples = 10, Shares = new() { new(Limiter.Gpu, 100) } }).ToList();
            var keptBottleneck = BottleneckRun.Normalize(bottleneck);
            Require(keptBottleneck.Count == BottleneckRun.Capacity && keptBottleneck[0].Date == start.AddDays(24)
                && keptBottleneck[^1].Date == start.AddDays(25 - BottleneckRun.Capacity), "Gargalo history");

            var stress = Enumerable.Range(0, 40).Select(i => new StressRun { Date = start.AddHours(i), Seconds = 60 }).ToList();
            var keptStress = StressRun.Normalize(stress);
            Require(keptStress.Count == StressRun.Capacity && keptStress[0].Date == start.AddHours(39), "Teste de carga history");

            var runs = Enumerable.Range(0, 15).Select(i => new BenchRun(start.AddDays(i), 1000 + i, 100, false, "CPU")).ToList();
            var keptRuns = BenchRun.Normalize(runs);
            Require(keptRuns.Count == BenchRun.Capacity && keptRuns[0].Date == start.AddDays(14), "Render history");

            var records = Enumerable.Range(0, 15).SelectMany(i => new[]
            {
                new BenchRecord(start.AddDays(i), "gpu", i, ""),
                new BenchRecord(start.AddDays(i).AddMinutes(1), "rt:alta:1440", i, ""),
            }).ToList();
            var keptRecords = BenchRecord.Normalize(records);
            Require(keptRecords.Count(r => r.Test == "gpu") == BenchRecord.CapacityPerTest
                && keptRecords.Count(r => r.Test == "rt:alta:1440") == BenchRecord.CapacityPerTest, "Benchmark history per test");
            Require(keptRecords.Zip(keptRecords.Skip(1)).All(p => p.First.Date >= p.Second.Date), "Benchmark history out of order");
            return new { bottleneck = keptBottleneck.Count, stress = keptStress.Count, runs = keptRuns.Count, records = keptRecords.Count };
        });

        Check("rules: settings migration drops removed overlay items and unknown report formats, and keeps a corrupt file", () =>
        {
            Require(OverlayMetrics.NormalizeItems(new[] { "fps", "vram_temp", "gpu_perfcap", "fps" }) is ["fps"], "Removed overlay items survived");
            Require(OverlayMetrics.NormalizeItems(null) == null, "Null overlay selection must stay null (default items)");

            using var folder = new CaseFolder();
            using var _ = AppPaths.OverrideDataFolder(folder.Path);
            Directory.CreateDirectory(AppSettings.Folder);
            string file = Path.Combine(AppSettings.Folder, "settings.json");
            File.WriteAllText(file, "{\"ReportFormat\":\"docx\"}");
            Require(AppSettings.Load().ReportFormat == "html", "Unknown report format survived");

            File.WriteAllText(file, "{ isto não é json");
            AppSettings.Load();
            Require(Directory.EnumerateFiles(AppSettings.Folder, "settings.json.invalid-*").Any(), "The corrupt file was not kept for recovery");
            return new { migrated = true };
        });

        Check("rules: battery health and power plan durations", () =>
        {
            Require(PowerStatus.EvaluateHealth(null, null).Verdict == HealthVerdict.Unknown, "No data");
            Require(PowerStatus.EvaluateHealth(55, 10).Verdict == HealthVerdict.Critical, "55% wear");
            Require(PowerStatus.EvaluateHealth(35, 10).Verdict == HealthVerdict.Warning, "35% wear");
            Require(PowerStatus.EvaluateHealth(10, 1200).Verdict == HealthVerdict.Warning, "1200 cycles");
            var used = PowerStatus.EvaluateHealth(20, 10);
            Require(used.Verdict == HealthVerdict.Good && used.Reason.Contains("normal"), "20% wear is normal");
            var fresh = PowerStatus.EvaluateHealth(5, 10);
            Require(fresh.Verdict == HealthVerdict.Good && fresh.Reason.Contains("ótimo"), "5% wear is great");

            var minutes = new (uint Seconds, string Text)[] { (0, "Nunca"), (59, "59 s"), (600, "10 min"), (3600, "1 h"), (5400, "1 h 30 min"), (3900, "1 h 05 min") };
            foreach (var (seconds, text) in minutes)
                Require(PowerPlans.Minutes(seconds) == text, $"{seconds} s became \"{PowerPlans.Minutes(seconds)}\", expected \"{text}\"");
            return new { durations = minutes.Length };
        });

        Check("rules: every app in the catalog has its official icon, and benchmark names match", () =>
        {
            string icons = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CoreBi.Desktop", "Assets", "Apps");
            if (!Directory.Exists(icons)) throw new DirectoryNotFoundException("Pasta de ícones não encontrada a partir de " + AppContext.BaseDirectory);
            var withoutIcon = new HashSet<string> { "vcredist", "directx" }; // componentes do Windows: ficam com o símbolo genérico
            var missing = AppCatalog.Create().Select(a => a.Icon).Distinct()
                .Where(icon => !withoutIcon.Contains(icon) && !File.Exists(Path.Combine(icons, icon + ".png"))).ToList();
            Require(missing.Count == 0, "Apps sem ícone em Assets/Apps: " + string.Join(", ", missing));

            Require(ReadoutFormat.BenchTitle("rt:alta:1440") == "Ray tracing", "Ray tracing key shown raw");
            Require(new[] { "render", "sim", "gpu", "mem", "disk" }.All(t => ReadoutFormat.BenchTitle(t) != t), "A benchmark without a display name");
            return new { apps = AppCatalog.Create().Count };
        });
    }
}
