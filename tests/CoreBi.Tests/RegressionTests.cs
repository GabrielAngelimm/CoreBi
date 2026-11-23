using System.IO;
using System.Text.Json;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Health;
using CoreBi.Engine.Monitoring.Power;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Persistence;
using CoreBi.Engine.Reports;
using CoreBi.Engine.Tools.Drivers;
using CoreBi.Engine.Tools.Startup;

internal static partial class Program
{
    private static void RegressionTests()
    {
        Check("disk health: USB temperature is never masked and wear/NVMe warnings trigger", () =>
        {
            // Pendrive/HD externo USB sem mídia SSD: limites de 55/65 °C; a leitura de 72 °C precisa aparecer como crítica.
            var hotUsb = DiskHealth.Evaluate(new DiskSample { Model = "External HDD", Bus = "USB", Temp = 72, Health = 0 });
            Require(hotUsb.Verdict == HealthVerdict.Critical && hotUsb.Disk?.Temperature == 72,
                $"Hot USB disk was masked: {hotUsb.Verdict} / {hotUsb.Reasons}");

            var warmSsd = DiskHealth.Evaluate(new DiskSample { Model = "SATA SSD", Bus = "SATA", Media = "SSD", Temp = 72, Health = 0 });
            Require(warmSsd.Verdict == HealthVerdict.Warning, $"72 °C on an SSD must warn, got {warmSsd.Verdict}");

            var worn = DiskHealth.Evaluate(new DiskSample { Model = "NVMe SSD", Bus = "NVMe", Media = "SSD", Wear = 96, Health = 0 });
            Require(worn.Verdict == HealthVerdict.Critical, $"96% wear must be Critical, got {worn.Verdict}");

            var healthy = DiskHealth.Evaluate(new DiskSample
            {
                Model = "Healthy NVMe",
                Bus = "NVMe",
                Media = "SSD",
                Temp = 42,
                TempMax = 58,
                Wear = 12,
                Health = 0,
            });
            Require(healthy.Verdict == HealthVerdict.Good && healthy.Disk?.TemperatureMax == 58,
                $"Healthy NVMe misclassified: {healthy.Verdict}");

            // O driver devolve zero ou o limite do disco (>= crítico) no lugar da máxima registrada: não é leitura.
            var bogusMax = DiskHealth.Evaluate(new DiskSample { Model = "NVMe", Bus = "NVMe", Media = "SSD", Temp = 40, TempMax = 100, Wear = 5, Health = 0 });
            var zeroMax = DiskHealth.Evaluate(new DiskSample { Model = "NVMe", Bus = "NVMe", Media = "SSD", Temp = 40, TempMax = 0, Wear = 5, Health = 0 });
            Require(bogusMax.Disk?.TemperatureMax == null && zeroMax.Disk?.TemperatureMax == null, "Invalid TempMax was shown as a recorded maximum");
            return new { hotUsb = hotUsb.Verdict.ToString(), warmSsd = warmSsd.Verdict.ToString(), worn = worn.Verdict.ToString() };
        });

        Check("bottleneck: classifier priority order and session distribution", () =>
        {
            // Priority: Vram > Thermal > Gpu > Cpu > Ram > Capped > Unknown
            var vramAndThermal = new LoadSample(98, 95, 99, "Core 0", 98, 96, 100, 100, true, 60);
            Require(vramAndThermal.Classify() == Limiter.Vram, "VRAM must take precedence over Thermal");
            var thermalAndGpu = new LoadSample(98, 95, 99, "Core 0", 50, 96, 96, 60, false, 60);
            Require(thermalAndGpu.Classify() == Limiter.Thermal, "Thermal must take precedence over GPU");
            var gpuAndCpu = new LoadSample(92, 90, 95, "Core 0", 50, 50, 70, 65, false, 60);
            Require(gpuAndCpu.Classify() == Limiter.Gpu, "GPU must take precedence over CPU");
            var cpuAndRam = new LoadSample(60, 50, 88, "Core 2", 50, 95, 65, 65, false, 60);
            Require(cpuAndRam.Classify() == Limiter.Cpu, "CPU max core must take precedence over RAM");
            var ramOnly = new LoadSample(60, 50, 60, "Core 0", 50, 93, 65, 65, false, 60);
            Require(ramOnly.Classify() == Limiter.Ram, "RAM limit must trigger");
            var capped = new LoadSample(50, 40, 50, "Core 0", 40, 50, 60, 55, false, 144);
            Require(capped.Classify() == Limiter.Capped, "Low usage with FPS must be Capped");

            var session = new BottleneckSession { Duration = TimeSpan.FromMinutes(1) };
            session.Add(gpuAndCpu);
            session.Add(gpuAndCpu);
            session.Add(cpuAndRam);
            Require(session.Count == 3 && session.Main == Limiter.Gpu, "Session Main limiter incorrect");
            var dist = session.Distribution();
            Require(dist.Count == 2 && dist[0].Limiter == Limiter.Gpu && Math.Abs(dist[0].Pct - 66.67) < 0.1, "Session distribution incorrect");
            return new { priorityVerified = true, main = session.Main.ToString() };
        });

        Check("naming: NVIDIA driver version preserves leading zeros", () =>
        {
            Require(HardwareNaming.NvidiaDriverVersion("32.0.15.0042") == "500.42", "Leading zero dropped in NVIDIA version");
            Require(HardwareNaming.NvidiaDriverVersion("32.0.15.6636") == "566.36", "Standard NVIDIA version failed");
            Require(HardwareNaming.NvidiaDriverVersion("31.0.15.5205") == "552.05", "Minor leading zero dropped");
            Require(HardwareNaming.NvidiaDriverVersion("not-a-version") == null, "Invalid version returned non-null");
            return new { leadingZeroPreserved = true };
        });

        Check("diagnostics: log redacts exception messages and rotates at 1 MiB", () =>
        {
            using var folder = new CaseFolder();
            using var _ = AppPaths.OverrideDataFolder(folder.Path);
            const string secret = "Bearer super-secret-token-12345";
            DiagnosticLog.Write("test.op", new InvalidOperationException(secret));
            string logPath = Path.Combine(DiagnosticLog.Folder, "corebi.log");
            string content = File.ReadAllText(logPath);
            Require(content.Contains("test.op") && content.Contains(nameof(InvalidOperationException)), "Log omitted operation or exception type");
            Require(!content.Contains(secret), "DiagnosticLog leaked exception message");

            // Force rotation at >= 1 MiB
            File.WriteAllBytes(logPath, new byte[1_048_576]);
            DiagnosticLog.Write("test.rotate", new IOException("rotated"));
            Require(File.Exists(logPath + ".previous") && new FileInfo(logPath).Length < 10_000, "DiagnosticLog did not rotate at 1 MiB");
            return new { messageRedacted = true, rotated = true };
        });

        Check("settings: corrupt JSON recovers defaults and out-of-range opacity falls back", () =>
        {
            using var folder = new CaseFolder();
            using var _ = AppPaths.OverrideDataFolder(folder.Path);
            Directory.CreateDirectory(AppSettings.Folder);
            string path = Path.Combine(AppSettings.Folder, "settings.json");

            File.WriteAllText(path, "{ invalid json truncated");
            var recovered = AppSettings.Load();
            Require(recovered.OverlayCorner == "TL" && recovered.OverlayScale == 1 && recovered.OverlayOpacity == 1, "Corrupt JSON did not recover defaults");

            File.WriteAllText(path, "{\"OverlayOpacity\":0.6}");
            Require(AppSettings.Load().OverlayOpacity == 0.6, "Valid opacity was altered");
            File.WriteAllText(path, "{\"OverlayOpacity\":0.05}");
            Require(AppSettings.Load().OverlayOpacity == 1.0, "Invisible overlay opacity was not reset to the default");
            File.WriteAllText(path, "{\"OverlayOpacity\":5.0}");
            Require(AppSettings.Load().OverlayOpacity == 1.0, "Out-of-range opacity was not reset to the default");
            return new { corruptRecovered = true, opacityNormalized = true };
        });

        Check("power history: state machine tracks boots, sleep, crashes and open session", () =>
        {
            var baseDay = new DateTime(2026, 10, 20, 0, 0, 0);
            var now = baseDay.AddDays(1).AddHours(10);
            var events = new[]
            {
                (baseDay.AddHours(8), PowerEventKind.Boot),
                (baseDay.AddHours(12), PowerEventKind.Off),
                (baseDay.AddHours(14), PowerEventKind.Resume),
                (baseDay.AddHours(14).AddMinutes(1), PowerEventKind.Resume), // duplicate resume ignored
                (baseDay.AddHours(22), PowerEventKind.Off),
                (baseDay.AddHours(23), PowerEventKind.Boot),
                (baseDay.AddHours(23).AddMinutes(1), PowerEventKind.Crash),
            };
            var history = PowerHistoryReader.Build(baseDay, now, baseDay.AddDays(-1), events);
            Require(history.Spans.Count == 3 && history.Spans[^1].Current, "Expected 2 closed spans + 1 current span");
            Require(history.OnTime(baseDay) == TimeSpan.FromHours(13), $"Day 1 on-time expected 13h, got {history.OnTime(baseDay)}");
            Require(history.OnTime(baseDay.AddDays(1)) == TimeSpan.FromHours(10), $"Day 2 on-time expected 10h, got {history.OnTime(baseDay.AddDays(1))}");
            Require(history.Unexpected.Count == 1, "Crash event not recorded");
            return new { spans = history.Spans.Count, crashes = history.Unexpected.Count };
        });

        Check("driver policy: AMD domains and vendor publishers validated strictly", () =>
        {
            Require(DriverDownloadPolicy.Validate("https://drivers.amd.com/drivers/whql.exe", "AMD").Host == "drivers.amd.com", "Official AMD host rejected");
            Expect<InvalidOperationException>(() => DriverDownloadPolicy.Validate("https://drivers.amd.com.evil.test/whql.exe", "AMD"));
            Expect<InvalidOperationException>(() => DriverDownloadPolicy.Validate("https://us.download.nvidia.com/x.exe", "AMD"));
            Require(DriverDownloadPolicy.IsPublisher("Advanced Micro Devices, Inc.", "AMD"), "Official AMD publisher rejected");
            Require(!DriverDownloadPolicy.IsPublisher("Unofficial Advanced Micro Devices, Inc.", "AMD"), "Spoofed AMD publisher accepted");
            return new { amdValidated = true };
        });

        Check("reports: JSON redacts sensitive metrics and handles duplicate keys", () =>
        {
            var data = new ReportData
            {
                Machine = "PC-PRIV",
                Disks =
                {
                    new ReportFinding(
                        Name: "SSD",
                        Details: "NVMe",
                        Verdict: "SAUDÁVEL",
                        Reasons: "",
                        Metrics: new List<KeyValuePair<string, string>>
                        {
                            new("Nº de série", "SECRET-SERIAL-99"),
                            new("Temperatura", "40 °C"),
                            new("Temperatura", "42 °C"),
                            new("Usuário", "alice"),
                        }),
                },
            };
            string json = ReportBuilder.Json(data);
            Require(!json.Contains("SECRET-SERIAL-99") && !json.Contains("alice"), "ReportBuilder.Json leaked sensitive metric values");
            using var doc = JsonDocument.Parse(json);
            var metrics = doc.RootElement.GetProperty("discos")[0].GetProperty("metricas");
            Require(metrics.GetProperty("Temperatura").GetString() == "40 °C"
                && metrics.GetProperty("Temperatura (2)").GetString() == "42 °C",
                "Duplicate metric key failed or lost second value");
            return new { redacted = true, duplicateKeySafe = true };
        });

        Check("startup: failed remove rolls back orphan backup and corrupt backup JSON is isolated", () =>
        {
            using var folder = new CaseFolder();
            string backupDir = Path.Combine(folder.Path, "backups");
            var store = new StartupBackupStore(backupDir);
            var failingBackend = new FailingRemoveStartupMutations();
            var service = new StartupService(failingBackend, store);

            Expect<IOException>(() => service.Remove(new StartupEntry { Name = "App" }));
            Require(service.Backups.Count == 0 && Directory.GetFiles(backupDir, "*.json").Length == 0,
                "Orphan backup remained after failed Remove()");

            // Corrupt JSON file alongside a valid one must not crash Backups listing
            File.WriteAllText(Path.Combine(backupDir, "corrupt.json"), "{ broken");
            string validPath = Path.Combine(folder.Path, "valid.txt");
            File.WriteAllText(validPath, "cmd");
            var working = new StartupService(new FileStartupMutations(validPath), store);
            working.Remove(new StartupEntry { Name = "Valid" });
            Require(working.Backups.Count == 1, $"Corrupt JSON broke backup listing (count={working.Backups.Count})");
            return new { rollbackClean = true, corruptIsolated = true };
        });
        Check("startup: friendly names, boot events and Store assets", () =>
        {
            Require(StartupManager.LooksLikeId("GoogleChromeAutoLaunch_579CF1F2F611610737FD8852B51") && StartupManager.LooksLikeId("{0F6A3B2C-1D4E-4F5A-8B9C-0D1E2F3A4B5C}")
                    && !StartupManager.LooksLikeId("Discord") && !StartupManager.LooksLikeId("EADM"), "Entry names that are ids");
            string xml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><EventData><Data Name='BootStartTime'>2026-11-22T19:10:17.000000000Z</Data>"
                         + "<Data Name='BootTime'>31842</Data><Data Name='MainPathBootTime'>17320</Data><Data Name='BootPostBootTime'>14522</Data><Data Name='BootNumStartupApps'>9</Data></EventData></Event>";
            var boot = BootTimes.Parse(xml, null) ?? throw new InvalidOperationException("Boot event not parsed");
            Require(boot.TotalMs == 31842 && boot.MainPathMs == 17320 && boot.PostBootMs == 14522 && boot.StartupApps == 9, "Boot event fields");
            Require(boot.When == new DateTime(2026, 11, 22, 19, 10, 17, DateTimeKind.Utc).ToLocalTime(), "Boot start time is UTC");
            Require(BootTimes.Parse(xml.Replace("31842", "0"), null) == null, "Empty boot accepted");
            using var folder = new CaseFolder();
            string assets = Directory.CreateDirectory(Path.Combine(folder.Path, "Assets")).FullName;
            File.WriteAllText(Path.Combine(assets, "Square44x44Logo.scale-200.png"), "");
            File.WriteAllText(Path.Combine(assets, "Square44x44Logo.targetsize-32.png"), "");
            Require(StoreStartup.AssetFile(folder.Path, @"Assets\Square44x44Logo.png")?.EndsWith("targetsize-32.png") == true, "Store icon picks the 32 px variant");
            return new { boot = boot.TotalMs };
        });

        Check("drivers: backup folders, packages and device problem codes", () =>
        {
            using var folder = new CaseFolder();
            string older = Directory.CreateDirectory(Path.Combine(folder.Path, "20261101-101010-abcdef12")).FullName;
            File.WriteAllText(Directory.CreateDirectory(Path.Combine(older, "nvlti.inf_amd64_1")).FullName + Path.DirectorySeparatorChar + "nvlti.inf", "");
            string newer = Directory.CreateDirectory(Path.Combine(folder.Path, "Drivers PC 2026-11-23 07h40")).FullName;
            foreach (string pkg in new[] { "a.inf_amd64_1", "b.inf_amd64_2" })
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(newer, pkg)).FullName, pkg.Split('_')[0]), "");
            Directory.SetCreationTime(older, new DateTime(2026, 11, 1));
            Directory.SetCreationTime(newer, new DateTime(2026, 11, 23));
            Require(DriverBackup.Find(newer)?.Count == 2, "Backup folder read directly");
            Require(DriverBackup.Find(folder.Path)?.Folder == newer, "Parent folder picks the newest backup");
            Require(DriverBackup.Find(Path.Combine(folder.Path, "missing")) == null && DriverBackup.Find(Directory.CreateDirectory(Path.Combine(folder.Path, "empty")).FullName) == null,
                "Folder without packages is not a backup");
            Require(DriverInventory.MsiSlug("MAG B550 TOMAHAWK (MS-7C91)") == "MAG-B550-TOMAHAWK" && DriverInventory.AsrockPlatform("B550M Pro4") == "AMD"
                && DriverInventory.AsrockPlatform("B660M Pro RS") == "Intel" && DriverInventory.AsrockPlatform("Fatal1ty") == null, "Board support page slugs");
            Require(DriverInventory.ProblemMeaning(28) == "Sem driver instalado" && DriverInventory.ProblemMeaning(999) == "Problema código 999", "Device problem text");
            return new { packages = 2 };
        });

        Check("formatting: Explorer capacities, vendor disk sizes and sentence-case verdicts", () =>
        {
            // 1 TB anunciado = 931 GB no Explorador (corta as casas acima de 100); o disco mostra a medida do fabricante.
            Require(CoreBi.Engine.Monitoring.Inventory.Fmt.Bytes(1_000_204_886_016) == "931 GB", "Capacity must floor like Explorer");
            Require(CoreBi.Engine.Monitoring.Inventory.Fmt.Bytes(81.3 * 1048576) == "81 MB", "MB capacities have no decimals");
            Require(ByteFormat.Decimal(500_107_862_016) == "500,1 GB" && ByteFormat.Decimal(32_000_000) == "32 MB", "Disk size uses vendor units");
            Require(HardwareNaming.Sentence("SAUDÁVEL") == "Saudável" && HardwareNaming.Sentence("SEM REDE") == "Sem rede"
                    && HardwareNaming.Sentence("temperatura") == "Temperatura", "Verdict labels must be sentence case");
            Require(WmiDiskTopology.MapBusType(3) == "ATA" && WmiDiskTopology.MapBusType(6) == "Fibre Channel", "Storage bus labels");
            return new { explorer = "931 GB", verdict = "Saudável" };
        });
    }

    private sealed class FailingRemoveStartupMutations : IStartupMutations
    {
        public StartupBackup Prepare(StartupEntry entry, string folder) => new() { Name = entry.Name, Command = "app.exe" };
        public void Remove(StartupEntry entry, StartupBackup snapshot) => throw new IOException("Injected remove failure");
        public void Restore(StartupBackup backup) { }
    }
}
