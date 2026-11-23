using System.Text.Json;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Network;
using CoreBi.Engine.Performance.Stress;

namespace CoreBi.Engine.Persistence;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks (Test = "gpu", "sim", "mem", "disk" ou "rt:&lt;qualidade&gt;:&lt;altura&gt;").</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary)
{
    /// <summary>Os números do resultado como a página mostra (nota e medidas), para ela abrir com o último teste; null nos antigos.</summary>
    public List<BenchFigure>? Figures { get; init; }
    /// <summary>O FPS ao longo da passada, reamostrado em até 120 pontos de progresso igual (Cena de jogo, Ray tracing, Simulação).</summary>
    public List<double>? Trace { get; init; }
}

/// <summary>Um número de resultado de benchmark: rótulo, valor já formatado, unidade, contexto, tom e a dica.</summary>
public sealed record BenchFigure(string Label, string Value, string Unit = "", string Sub = "", string Tone = "", string? Tip = null);

/// <summary>
/// Configurações em %AppData%\CoreBi. Campos removidos do app (como a antiga chave HowManyFpsKeyProtected) são
/// ignorados ao carregar e descartados na próxima gravação. Backups antigos de inicialização em StartupRemoved são
/// preservados como JSON opaco, sem conversão automática em comandos elevados.
/// </summary>
public sealed class AppSettings
{
    internal static string Folder => AppPaths.DataFolder;
    private static string FilePath => Path.Combine(Folder, "settings.json");

    // Benchmark: últimos resultados, mais recente primeiro (Normalize corta pelo fim).
    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();
    public List<JsonElement> StartupRemoved { get; set; } = new();
    /// <summary>Testes de rede concluídos, mais recente primeiro (NetworkHistory.Capacity).</summary>
    public List<NetworkRun> NetRuns { get; set; } = new();
    /// <summary>Análises de sessão do Gargalo, mais recente primeiro (BottleneckRun.Capacity).</summary>
    public List<BottleneckRun> BottleneckRuns { get; set; } = new();
    /// <summary>Testes de carga encerrados, mais recente primeiro (StressRun.Capacity).</summary>
    public List<StressRun> StressRuns { get; set; } = new();

    public bool LightTheme { get; set; }

    public string OverlayCorner { get; set; } = OverlayMetrics.DefaultCorner;
    public double OverlayScale { get; set; } = OverlayMetrics.DefaultScale;
    public double OverlayOpacity { get; set; } = OverlayMetrics.DefaultOpacity;
    /// <summary>Itens escolhidos para o overlay (chaves de OverlayMetrics); null = padrão.</summary>
    public List<string>? OverlayItems { get; set; }
    /// <summary>Controla a oferta única de "Nome do hardware" para seleções salvas anteriores.</summary>
    public bool OverlayNamesOffered { get; set; }

    // Relatório: a última escolha da janela de exportação, para a próxima abrir igual.
    /// <summary>Formato do relatório: "html", "pdf" ou "json".</summary>
    public string ReportFormat { get; set; } = "html";
    /// <summary>Pasta onde o último relatório foi salvo; null = Documentos.</summary>
    public string? ReportFolder { get; set; }
    /// <summary>Abrir o relatório logo depois de salvar.</summary>
    public bool ReportOpen { get; set; } = true;

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
        NetRuns = NetworkHistory.Normalize(NetRuns);
        BottleneckRuns = BottleneckRun.Normalize(BottleneckRuns);
        StressRuns = StressRun.Normalize(StressRuns);
        StartupRemoved = (StartupRemoved ?? new()).Where(x => x.ValueKind != JsonValueKind.Null && x.ValueKind != JsonValueKind.Undefined).ToList();
        OverlayCorner = OverlayMetrics.NormalizeCorner(OverlayCorner);
        OverlayScale = OverlayMetrics.NormalizeScale(OverlayScale);
        OverlayOpacity = OverlayMetrics.NormalizeOpacity(OverlayOpacity);
        OverlayItems = OverlayMetrics.NormalizeItems(OverlayItems);
        ReportFormat = ReportFormat is "html" or "pdf" or "json" ? ReportFormat : "html";
    }
}
