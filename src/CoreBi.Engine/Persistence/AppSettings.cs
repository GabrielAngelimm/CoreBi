using CoreBi.Engine.Tools.Startup;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Persistence;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks de jogo (Test = "gpu", "sim" ou "mem"). Score é usado para a barra do histórico.</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary);

/// <summary>Configurações em %AppData%\CoreBi. A chave da API é cifrada com DPAPI (só este usuário do Windows a lê).</summary>
public sealed class AppSettings
{
    public static string Folder { get; } =
        Environment.GetEnvironmentVariable("COREBI_DATA_DIR") is { Length: > 0 } custom
            ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoreBi");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public string? HowManyFpsKeyProtected { get; set; }

    // Benchmark: últimos resultados (mais recente primeiro)
    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();

    /// <summary>Programas de inicialização removidos, para "Restaurar removidos".</summary>
    public List<StartupBackup> StartupRemoved { get; set; } = new();

    // Aparência
    public bool LightTheme { get; set; }

    // Overlay
    public string OverlayCorner { get; set; } = "TL";
    public double OverlayScale { get; set; } = 1.0;
    /// <summary>Itens escolhidos para o overlay (chaves de OverlayMetrics); null = padrão.</summary>
    public List<string>? OverlayItems { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] // nunca gravar a chave em texto puro
    public string? HowManyFpsKey
    {
        get
        {
            if (string.IsNullOrEmpty(HowManyFpsKeyProtected)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(HowManyFpsKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }
        set
        {
            HowManyFpsKeyProtected = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

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
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"), overwrite: false);
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
        BenchRuns ??= new(); BenchRecords ??= new(); StartupRemoved ??= new();
        BenchRuns = BenchRuns.Where(x => x != null).Take(100).ToList();
        BenchRecords = BenchRecords.Where(x => x != null).Take(1000).ToList();
        StartupRemoved = StartupRemoved.Where(x => x != null).ToList();
        if (OverlayCorner is not ("TL" or "TR" or "BL" or "BR")) OverlayCorner = "TL";
        if (!double.IsFinite(OverlayScale) || OverlayScale is < 0.5 or > 3) OverlayScale = 1;
        OverlayItems = OverlayItems?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(100).ToList();
    }
}
