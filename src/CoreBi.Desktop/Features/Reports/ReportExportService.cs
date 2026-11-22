using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using CoreBi.Desktop.Features.Monitoring.Inventory;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring;
using CoreBi.Engine.Monitoring.Health;
using CoreBi.Engine.Monitoring.Power;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Persistence;
using CoreBi.Engine.Reports;

namespace CoreBi.Desktop.Features.Reports;

/// <summary>Exporta o relatório completo do sistema em HTML, PDF (via Edge headless) ou JSON.</summary>
internal static class ReportExportService
{
    /// <summary>Chaves das partes do diagnóstico na janela de exportação (as fichas usam as chaves de SystemSheets).</summary>
    public const string Disks = "disks", Power = "power", Network = "network", Sensors = "sensors", Bench = "bench";

    /// <summary>As partes que a janela oferece: as seis fichas, na ordem do relatório, e os blocos do diagnóstico.</summary>
    public static (List<ReportPart> Sheets, List<ReportPart> Diagnostics) Parts(
        FeatureContext context, (List<IndexItem> Index, Dictionary<string, Sheet> Sheets)? builtSheets, bool networkTested)
    {
        var sheets = new List<ReportPart>();
        if (builtSheets is { } built)
        {
            foreach (var key in SystemReport.Order)
            {
                if (built.Index.FirstOrDefault(i => i.Key == key) is not { } item || !built.Sheets.ContainsKey(key)) continue;
                sheets.Add(new ReportPart { Key = key, Title = item.Category, Caption = item.Name, Icon = item.Glyph, Channel = item.Channel, Tip = item.Tip });
            }
        }

        int sensors = context.Sensors.Snapshot().Count;
        int bench = context.Settings.BenchRecords.Count + context.Settings.BenchRuns.Count;
        var diag = new List<ReportPart>
        {
            new() { Key = Disks, Title = "Saúde dos discos", Caption = "Vida útil, temperatura e erros", Icon = "storage", Channel = "Storage" },
            new() { Key = Power, Title = "Bateria e energia", Caption = "Fonte, bateria e desgaste", Icon = "\uE7E8" },
            new() { Key = Network, Title = "Teste de rede", Icon = "net", Channel = "Net", Available = networkTested,
                    Caption = networkTested ? "O último teste desta sessão" : "Faça o teste em Rede antes" },
            new() { Key = Sensors, Title = "Sensores", Icon = "\uE9D9", Available = sensors > 0,
                    Caption = sensors > 0 ? $"{sensors} leituras, com mínimo e máximo" : "Nenhum sensor respondeu" },
            new() { Key = Bench, Title = "Benchmarks e testes", Icon = "\uE8B9", Available = bench > 0,
                    Caption = bench > 0 ? $"{bench} {(bench == 1 ? "resultado salvo" : "resultados salvos")}" : "Nenhum teste feito ainda" },
        };
        return (sheets, diag);
    }

    /// <summary>Gera o relatório com as partes escolhidas no arquivo escolhido; devolve o caminho salvo.</summary>
    public static async Task<string> ExportAsync(
        FeatureContext context,
        (List<IndexItem> Index, Dictionary<string, Sheet> Sheets)? builtSheets,
        IReadOnlyList<InfoNode> systemNodes,
        IReadOnlyList<HealthCard>? networkResult,
        ReportChoice choice,
        Action<string> onStatus)
    {
        string path = choice.Path;
        string ext = choice.Format;
        var parts = choice.Parts;
        onStatus(ext == "pdf" ? "Gerando PDF…" : "Gerando relatório…");

        // Dados que vivem na interface são copiados aqui (thread da tela); a leitura dos discos roda em segundo plano.
        // Só o que foi escolhido é lido: sem "Saúde dos discos", nem o SMART é consultado.
        var sensorsSnapshot = context.Sensors.Snapshot();
        var index = builtSheets?.Index.Where(i => parts.Contains(i.Key)).ToList();
        var data = new ReportData
        {
            Date = DateTime.Now,
            Machine = Environment.MachineName,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "",
            // logo-dark.png é o logo para fundo claro (traço escuro); logo.png, para fundo escuro.
            LogoBase64 = LoadLogoBase64("logo-dark.png"),
            LogoOnDarkBase64 = LoadLogoBase64("logo.png"),
            Dark = !ThemeManager.IsLight,
            Sheets = builtSheets is { } built && index != null ? SystemReport.From(index, built.Sheets) : new(),
            // A árvore bruta (só no JSON) acompanha as fichas; as placas de vídeo, a ficha delas
            System = index is { Count: > 0 } ? systemNodes.Select(SystemReport.ToNode).Where(n => n != null).Cast<ReportNode>().ToList() : new(),
            Gpus = parts.Contains(SystemSheets.Gpu)
                ? context.Gpus.Select(g => new ReportGpu(g.Name, g.Subtitle,
                    g.Sections.Select(SystemReport.ToNode).Where(n => n != null).Cast<ReportNode>().ToList())).ToList()
                : new(),
            Sensors = parts.Contains(Sensors)
                ? sensorsSnapshot.Select(s => new ReportSensor(s.Hardware, s.Category, s.Name, s.Value, s.Min, s.Max)).ToList()
                : new(),
            Benchmarks = parts.Contains(Bench) ? BuildBenchList(context.Settings) : new(),
        };

        if (parts.Contains(Disks))
        {
            var nvme = context.Sensors.NvmeLogs();
            var disks = await Task.Run(() => DiskHealth.Read(sensorsSnapshot, nvme));
            data.Disks.AddRange(disks.Select(d => new ReportFinding(d.Title, d.Meta, d.VerdictText, d.Reasons, d.Metrics, ToneOf(d.Verdict))));
        }
        if (parts.Contains(Power))
        {
            var power = await Task.Run(() => BatteryInfo.Read(sensorsSnapshot));
            data.Power.AddRange(power.Select(d => new ReportFinding(d.Title, d.Meta, d.VerdictText, string.Join(" ", d.Reasons), d.Metrics, ToneOf(d.Verdict))));
        }
        if (parts.Contains(Network) && networkResult != null)
            data.Network.AddRange(networkResult.Select(d => new ReportFinding(d.Title, d.Meta, d.VerdictText, string.Join(" ", d.Reasons), d.Metrics, ToneOf(d.Verdict))));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (ext == "pdf") await PdfPrinter.PrintAsync(ReportBuilder.Html(data), path, context.Lifetime);
        else await File.WriteAllTextAsync(path, ext == "json" ? ReportBuilder.Json(data) : ReportBuilder.Html(data), new UTF8Encoding(true), context.Lifetime);

        if (choice.Open)
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        return path;
    }

    private static string ToneOf(HealthVerdict v) => v switch
    {
        HealthVerdict.Good => "ok",
        HealthVerdict.Warning => "warn",
        HealthVerdict.Critical => "danger",
        _ => "",
    };

    private static List<ReportBench> BuildBenchList(AppSettings settings)
    {
        static string TestName(string t) => t switch
        {
            "gpu" => "Cena de jogo (GPU)",
            "sim" => "Simulação de jogo (CPU)",
            "mem" => "Memória e carregamento",
            "disk" => "Disco",
            _ => t,
        };
        var list = settings.BenchRecords.Select(r => new ReportBench(r.Date, TestName(r.Test), r.Summary)).ToList();
        foreach (var r in settings.BenchRuns)
        {
            var parts = new List<string>();
            if (r.Multi is double mt) parts.Add($"multi-core {mt:N0} pts");
            if (r.Single is double st) parts.Add($"single-core {st:N0} pts");
            if (r.Sustained) parts.Add("sustentado");
            list.Add(new ReportBench(r.Date, "Render (CPU)", string.Join(" · ", parts) + (string.IsNullOrEmpty(r.Cpu) ? "" : $" · {r.Cpu}")));
        }
        return list.OrderByDescending(b => b.Date).ToList();
    }

    private static string? LoadLogoBase64(string file)
    {
        try
        {
            var info = Application.GetResourceStream(new Uri($"pack://application:,,,/CoreBi.Desktop;component/Assets/{file}"));
            using var ms = new MemoryStream();
            info.Stream.CopyTo(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }
}
