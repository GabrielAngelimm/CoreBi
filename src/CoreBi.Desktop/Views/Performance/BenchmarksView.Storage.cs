using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Performance.Benchmarks;

namespace CoreBi.Desktop.Views;

public partial class BenchmarksView
{
    private void InitDiskBench()
    {
        if (_gameBenchRunning) return;
        string? keep = Ui.CheckedTag(DiskDrivePanel);
        DiskDrivePanel.Children.Clear();
        var drives = DiskBench.Drives();
        if (drives.Count == 0)
        {
            DiskStatus.Text = "Nenhuma unidade com 1,5 GB livres.";
            DiskDetail.Text = "";
            BtnDiskBench.IsEnabled = false;
            UpdateDiskPlate();
            return;
        }
        BtnDiskBench.IsEnabled = true;
        string pick = keep != null && drives.Any(d => d.Root == keep) ? keep
            : drives.FirstOrDefault(d => d.Root.StartsWith("C", StringComparison.OrdinalIgnoreCase)).Root ?? drives[0].Root;
        foreach (var (root, text) in drives)
        {
            var chip = new RadioButton
            {
                Content = text,
                Tag = root,
                GroupName = "DiskDrive",
                Style = (Style)FindResource("Chip"),
                IsChecked = root == pick,
            };
            chip.Checked += (_, _) => UpdateDiskPlate();
            DiskDrivePanel.Children.Add(chip);
        }
        UpdateDiskPlate();
    }

    private void UpdateDiskPlate()
    {
        var chip = DiskDrivePanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (chip?.Tag is not string root) { DiskDevice.Text = "Unidade"; DiskDeviceSpec.Text = ""; return; }
        DiskDevice.Text = $"Unidade {chip.Content}";
        try
        {
            var d = new DriveInfo(root);
            DiskDeviceSpec.Text = $"{d.TotalSize / 1e9:N0} GB, {d.TotalFreeSpace / 1e9:N0} GB livres";
        }
        catch { DiskDeviceSpec.Text = ""; }
    }

    private static List<MemRow> DiskRows(DiskBenchResult? r)
    {
        static string N(double? v) => v is double d ? $"{d:N0}" : "—";
        return new()
        {
            new("Leitura sequencial", N(r?.SeqReadMBs), "MiB/s", "Blocos de 1 MB", "Pesa ao copiar arquivos grandes e ao carregar jogos."),
            new("Escrita sequencial", N(r?.SeqWriteMBs), "MiB/s", "Blocos de 1 MB, direto no disco",
                "SSDs com cache SLC ficam mais lentos depois de gravar dezenas de GB seguidos."),
            new("Leitura aleatória de 4 KB", N(r?.ReadIopsQ1), "IOPS", r is null ? "Uma por vez" : $"Uma por vez, latência média de {r.ReadLatencyUs:0.0} µs",
                "É o que mais pesa na rapidez do sistema e dos programas."),
            new("Escrita aleatória de 4 KB", N(r?.WriteIopsQ1), "IOPS", r is null ? "Uma por vez" : $"Uma por vez, latência média de {r.WriteLatencyUs:0.0} µs"),
            new("Leitura aleatória de 4 KB em paralelo", N(r?.ReadIopsQ8), "IOPS", "8 ao mesmo tempo", "Como ao abrir muitos programas de uma vez."),
        };
    }

    private async void BtnDiskBench_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null) return;
        if (_context.Workloads.ActiveOperation == WorkloadOperations.DiskIo) { _gameBenchCts?.Cancel(); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.DiskIo);
        if (workload == null) { DiskStatus.Text = "Termine o outro teste antes."; DiskDetail.Text = ""; return; }

        string root = Ui.CheckedTag(DiskDrivePanel) ?? "";
        if (root.Length == 0) { DiskStatus.Text = "Escolha uma unidade."; DiskDetail.Text = ""; return; }
        long fileMb = long.Parse(Ui.CheckedTag(DiskSizePanel) ?? "1024", CultureInfo.InvariantCulture);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        _gameBenchCts = cts;
        var ct = cts.Token;
        SetGameBenchRunning(BtnDiskBench, true);
        DiskDrivePanel.IsEnabled = DiskSizePanel.IsEnabled = false;
        DiskResult.ItemsSource = DiskRows(null);
        (string Phase, double Frac) progress = ("Preparando", 0);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            DiskStatus.Text = progress.Phase;
            DiskDetail.Text = $"{progress.Frac * 100:0} %";
            DiskProgress.ScaleX = progress.Frac;
        }, Dispatcher);
        timer.Start();

        try
        {
            _context.SetBenchmarkIsolation(true, false);
            var r = await Task.Run(() => DiskBench.Run(root, fileMb, (phase, f) => progress = (phase, f), ct), ct);
            ct.ThrowIfCancellationRequested();

            DiskResult.ItemsSource = DiskRows(r);
            DiskStatus.Text = "Concluído";
            DiskDetail.Text = "Arquivo de teste apagado";
            SaveBenchRecord("disk", r.Score,
                $"{r.Drive} · {r.SeqReadMBs:N0}/{r.SeqWriteMBs:N0} MiB/s · 4K {r.ReadIopsQ1 / 1000:0.#}k IOPS");
        }
        catch (OperationCanceledException) { DiskStatus.Text = "Cancelado"; DiskDetail.Text = "Arquivo de teste apagado"; }
        catch (Exception ex)
        {
            var inner = (ex as AggregateException)?.InnerException ?? ex;
            DiskStatus.Text = $"Falhou: {inner.Message}";
            DiskDetail.Text = "";
        }
        finally
        {
            _context.SetBenchmarkIsolation(false, false);
            timer.Stop();
            DiskProgress.ScaleX = 0;
            DiskDrivePanel.IsEnabled = DiskSizePanel.IsEnabled = true;
            SetGameBenchRunning(BtnDiskBench, false);
            _gameBenchCts = null;
        }
    }

    private static List<MemRow> MemRows(double? lat, (double Read, double Copy)? bw, LoadResult? load)
    {
        static string F(double? v, string format) => v is double d ? d.ToString(format, CultureInfo.CurrentCulture) : "—";
        string drive = load?.Drive.TrimEnd('\\') ?? "";
        return new()
        {
            new("Latência da RAM", F(lat, "0.0"), "ns", "", "Acesso aleatório à memória. Quanto menor, melhor."),
            new("Leitura da RAM", F(bw?.Read, "0.0"), "GB/s", "", "Todas as threads lendo em sequência."),
            new("Cópia da RAM", F(bw?.Copy, "0.0"), "GB/s", "", "Leitura e escrita ao mesmo tempo."),
            new("Leitura do pacote do mapa", F(load?.SeqReadMBs, "N0"), "MiB/s", load is null ? "" : $"512 MB em sequência na unidade {drive}",
                "Leitura sequencial, sem o cache do Windows."),
            new("Arquivos pequenos", F(load?.FilesPerSec, "N0"), "arq/s", load is null ? "" : $"{load.SmallFilesMBs:N0} MiB/s",
                "Mil arquivos de 64 KB lidos por 8 threads, sem o cache do Windows."),
            new("Carregamento do mapa", F(load?.MapLoadSeconds, "0.00"), "s", "", "Pacote e arquivos pequenos juntos. Quanto menor, mais rápido o jogo carrega."),
        };
    }

    private async void BtnMemBench_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null) return;
        if (_context.Workloads.ActiveOperation == WorkloadOperations.RamAndLoading) { _gameBenchCts?.Cancel(); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.RamAndLoading);
        if (workload == null) { MemStatus.Text = "Termine o outro teste antes."; MemDetail.Text = ""; return; }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        _gameBenchCts = cts;
        var ct = cts.Token;
        SetGameBenchRunning(BtnMemBench, true);
        double? lat = null;
        (double Read, double Copy)? bw = null;
        LoadResult? load = null;
        void Show() => MemResult.ItemsSource = MemRows(lat, bw, load);
        Show();
        (string Phase, double Frac) progress = ("", 0);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            MemStatus.Text = progress.Phase;
            MemDetail.Text = $"{progress.Frac * 100:0} %";
            MemProgress.ScaleX = progress.Frac;
        }, Dispatcher);
        timer.Start();

        try
        {
            progress = ("Medindo a latência da RAM", 0.05);
            lat = await Task.Run(() => MemoryBench.MeasureLatencyNs(ct), ct);
            ct.ThrowIfCancellationRequested();
            Show();

            progress = ("Medindo a banda da RAM", 0.2);
            bw = await Task.Run(() => MemoryBench.MeasureBandwidth(ct), ct);
            ct.ThrowIfCancellationRequested();
            Show();

            load = await Task.Run(() => LoadingBench.Measure((phase, f) => progress = (phase, 0.35 + f * 0.6), ct), ct);
            Show();

            MemStatus.Text = "Concluído";
            MemDetail.Text = "Arquivos de teste apagados";
            SaveBenchRecord("mem", MemoryBench.Score(lat.Value, load.MapLoadSeconds),
                $"{lat:0} ns · {bw.Value.Read:0} GB/s · mapa {load.MapLoadSeconds:0.00} s");
        }
        catch (OperationCanceledException) { MemStatus.Text = "Cancelado"; MemDetail.Text = "Arquivos de teste apagados"; }
        catch (Exception ex) { MemStatus.Text = $"Falhou: {ex.Message}"; MemDetail.Text = ""; }
        finally
        {
            timer.Stop();
            MemProgress.ScaleX = 0;
            SetGameBenchRunning(BtnMemBench, false);
            _gameBenchCts = null;
        }
    }
}
