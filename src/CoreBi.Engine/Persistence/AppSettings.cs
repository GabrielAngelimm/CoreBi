using System.Text.Json;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Persistence;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks (Test = "gpu", "sim", "mem", "disk" ou "rt:&lt;qualidade&gt;:&lt;altura&gt;").</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary);

/// <summary>
/// Configurações em %AppData%\CoreBi. Campos removidos do app (como a antiga chave HowManyFpsKeyProtected) são
/// ignorados ao carregar e descartados na próxima gravação. Backups antigos de inicialização em StartupRemoved são
/// preservados como JSON opaco, sem conversão automática em comandos elevados.
/// </summary>
public sealed class AppSettings
{
    public static string Folder => AppPaths.DataFolder;
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();
    public List<JsonElement> StartupRemoved { get; set; } = new();

    public bool LightTheme { get; set; }

    public string OverlayCorner { get; set; } = OverlayMetrics.DefaultCorner;
    public double OverlayScale { get; set; } = OverlayMetrics.DefaultScale;
    public double OverlayOpacity { get; set; } = OverlayMetrics.DefaultOpacity;
    public List<string>? OverlayItems { get; set; }
    /// <summary>Controla a oferta única de "Nome do hardware" para seleções salvas anteriores.</summary>
    public bool OverlayNamesOffered { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                if (new FileInfo(FilePath).Length > 2_097_152) throw new JsonException("Arquivo de configuração excede o limite.");
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("settings.load", ex);
            try
            {
                if (File.Exists(FilePath))
                    File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"), overwrite: false);
            }
            catch (Exception backupError) { DiagnosticLog.Write("settings.recovery-copy", backupError); }
        }
        return new AppSettings();
    }

    public void Save()
    {
        Normalize();
        AtomicJsonFile.Write(FilePath, this);
    }

    private void Normalize()
    {
        BenchRuns = (BenchRuns ?? new()).Where(x => x != null).Take(100).ToList();
        BenchRecords = (BenchRecords ?? new()).Where(x => x != null).Take(1000).ToList();
        StartupRemoved = (StartupRemoved ?? new()).Where(x => x.ValueKind != JsonValueKind.Null && x.ValueKind != JsonValueKind.Undefined).ToList();
        OverlayCorner = OverlayMetrics.NormalizeCorner(OverlayCorner);
        OverlayScale = OverlayMetrics.NormalizeScale(OverlayScale);
        OverlayOpacity = OverlayMetrics.NormalizeOpacity(OverlayOpacity);
        OverlayItems = OverlayMetrics.NormalizeItems(OverlayItems);
    }
}
