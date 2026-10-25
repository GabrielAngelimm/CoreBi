using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Power;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Reports;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    internal async void BtnReport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar relatório",
            Filter = "Relatório HTML (*.html)|*.html|Dados JSON (*.json)|*.json",
            DefaultExt = ".html",
            FileName = $"CoreBi-relatorio-{DateTime.Now:yyyyMMdd-HHmm}",
        };
        if (dlg.ShowDialog(this) != true) return;
        string path = dlg.FileName;
        bool json = string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

        BtnReport.IsEnabled = false;
        BtnReport.Content = "Gerando…";
        try
        {
            // Dados que vivem na interface são copiados aqui (thread da tela); a leitura dos discos roda em segundo plano.
            var sensorsSnapshot = _monitor.Snapshot();
            var data = new ReportData
            {
                Date = DateTime.Now,
                Machine = Environment.MachineName,
                AppVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "",
                LogoBase64 = LoadLogoBase64(),
                System = PageSystem.Nodes
                         .Select(ReportBuilder.ToNode).Where(n => n != null).Cast<ReportNode>().ToList(),
                Gpus = _gpus.Select(g => new ReportGpu(g.Name, g.Subtitle,
                         g.Sections.Select(ReportBuilder.ToNode).Where(n => n != null).Cast<ReportNode>().ToList())).ToList(),
                Sensors = sensorsSnapshot.Select(s => new ReportSensor(s.Hardware, s.Category, s.Name, s.Value, s.Min, s.Max)).ToList(),
                Benchmarks = BuildBenchList(),
            };
            var disks = await Task.Run(() => DiskHealth.Read(sensorsSnapshot));
            data.Disks.AddRange(disks.Select(d => new ReportDisk(d.Title, d.Meta, d.VerdictText, d.Reasons, d.Metrics)));
            var power = await Task.Run(() => BatteryInfo.Read(sensorsSnapshot));
            data.Power.AddRange(power.Select(d => new ReportDisk(d.Title, d.Meta, d.VerdictText, d.Reasons, d.Metrics)));
            if (PageNet.LastResult is { } network)
                data.Network.AddRange(network.Select(d => new ReportDisk(d.Title, d.Meta, d.VerdictText, d.Reasons, d.Metrics)));

            string text = json ? ReportBuilder.Json(data) : ReportBuilder.Html(data);
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(true));

            SystemSub.Text = $"Relatório salvo em {path}";
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível gerar o relatório:\n" + ex.Message, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnReport.Content = "Exportar relatório";
            BtnReport.IsEnabled = true;
        }
    }

    internal List<ReportBench> BuildBenchList()
    {
        string TestName(string t) => t switch
        {
            "gpu" => "Cena de jogo (GPU)",
            "sim" => "Simulação de jogo (CPU)",
            "mem" => "Memória e carregamento",
            "disk" => "Disco",
            _ => t
        };
        var list = _settings.BenchRecords.Select(r => new ReportBench(r.Date, TestName(r.Test), r.Summary)).ToList();
        foreach (var r in _settings.BenchRuns)
        {
            var parts = new List<string>();
            if (r.Multi is double mt) parts.Add($"multi-core {mt:N0} pts");
            if (r.Single is double st) parts.Add($"single-core {st:N0} pts");
            if (r.Sustained) parts.Add("sustentado");
            list.Add(new ReportBench(r.Date, "Render (CPU)", string.Join(" · ", parts) + (string.IsNullOrEmpty(r.Cpu) ? "" : $" · {r.Cpu}")));
        }
        return list.OrderByDescending(b => b.Date).ToList();
    }

    internal static string? LoadLogoBase64()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/CoreBi.Desktop;component/Assets/logo-badge.png"));
            using var ms = new MemoryStream();
            info.Stream.CopyTo(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }
}
