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
    public static async Task<string?> ExportInteractiveAsync(
        FeatureContext context,
        (List<IndexItem> Index, Dictionary<string, Sheet> Sheets)? builtSheets,
        IReadOnlyList<InfoNode> systemNodes,
        IReadOnlyList<HealthCard>? networkResult,
        Action<string> onStatus)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar relatório",
            Filter = "Relatório para ler e imprimir (HTML)|*.html|Relatório em PDF (A4)|*.pdf|Dados (JSON)|*.json",
            DefaultExt = ".html",
            AddExtension = true,
            FileName = $"CoreBi-{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd-HHmm}",
        };
        if (dlg.ShowDialog(context.Owner) != true) return null;

        string path = dlg.FileName;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        // A extensão digitada manda; sem uma conhecida, vale o tipo escolhido na lista.
        if (ext is not (".html" or ".htm" or ".pdf" or ".json"))
        {
            ext = dlg.FilterIndex switch { 2 => ".pdf", 3 => ".json", _ => ".html" };
            path += ext;
        }

        onStatus(ext == ".pdf" ? "Gerando PDF…" : "Gerando relatório…");

        // Dados que vivem na interface são copiados aqui (thread da tela); a leitura dos discos roda em segundo plano.
        var sensorsSnapshot = context.Sensors.Snapshot();
        var data = new ReportData
        {
            Date = DateTime.Now,
            Machine = Environment.MachineName,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "",
            // logo-dark.png é o logo para fundo claro (traço escuro); logo.png, para fundo escuro.
            LogoBase64 = LoadLogoBase64("logo-dark.png"),
            LogoOnDarkBase64 = LoadLogoBase64("logo.png"),
            Dark = !ThemeManager.IsLight,
            Sheets = builtSheets is { } built ? SystemReport.From(built.Index, built.Sheets) : new(),
            System = systemNodes.Select(SystemReport.ToNode).Where(n => n != null).Cast<ReportNode>().ToList(),
            Gpus = context.Gpus.Select(g => new ReportGpu(g.Name, g.Subtitle,
                g.Sections.Select(SystemReport.ToNode).Where(n => n != null).Cast<ReportNode>().ToList())).ToList(),
            Sensors = sensorsSnapshot.Select(s => new ReportSensor(s.Hardware, s.Category, s.Name, s.Value, s.Min, s.Max)).ToList(),
            Benchmarks = BuildBenchList(context.Settings),
        };

        var nvme = context.Sensors.NvmeLogs();
        var disks = await Task.Run(() => DiskHealth.Read(sensorsSnapshot, nvme));
        data.Disks.AddRange(disks.Select(d => new ReportFinding(d.Title, d.Meta, d.VerdictText, d.Reasons, d.Metrics, ToneOf(d.Verdict))));
        var power = await Task.Run(() => BatteryInfo.Read(sensorsSnapshot));
        data.Power.AddRange(power.Select(d => new ReportFinding(d.Title, d.Meta, d.VerdictText, string.Join(" ", d.Reasons), d.Metrics, ToneOf(d.Verdict))));
        if (networkResult != null)
            data.Network.AddRange(networkResult.Select(d => new ReportFinding(d.Title, d.Meta, d.VerdictText, string.Join(" ", d.Reasons), d.Metrics, ToneOf(d.Verdict))));

        if (ext == ".pdf") await PdfPrinter.PrintAsync(ReportBuilder.Html(data), path, context.Lifetime);
        else await File.WriteAllTextAsync(path, ext == ".json" ? ReportBuilder.Json(data) : ReportBuilder.Html(data), new UTF8Encoding(true), context.Lifetime);

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
