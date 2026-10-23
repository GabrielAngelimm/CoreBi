using CoreBi.Engine.Performance.Benchmarks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    internal void InitDiskBench()
    {
        if (_gameBenchRunning) return; // não mexe nos botões durante um teste
        string? keep = CheckedTag(DiskDrivePanel);
        DiskDrivePanel.Children.Clear();
        var drives = DiskBench.Drives();
        if (drives.Count == 0)
        {
            DiskStatus.Text = "Nenhuma unidade com espaço livre suficiente (1,5 GB).";
            BtnDiskBench.IsEnabled = false;
            return;
        }
        BtnDiskBench.IsEnabled = true;
        string pick = keep != null && drives.Any(d => d.Root == keep) ? keep
                    : drives.FirstOrDefault(d => d.Root.StartsWith("C", StringComparison.OrdinalIgnoreCase)).Root ?? drives[0].Root;
        foreach (var (root, text) in drives)
            DiskDrivePanel.Children.Add(new RadioButton
            {
                Content = text,
                Tag = root,
                GroupName = "DiskDrive",
                Style = (Style)FindResource("Chip"),
                IsChecked = root == pick,
            });
    }

    internal async void BtnDiskBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "Disk IO") { _gameBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { DiskStatus.Text = "Termine o outro teste antes."; return; }
        using var workload = _workloads.TryBegin("Disk IO");
        if (workload == null) { DiskStatus.Text = "Termine o outro teste antes."; return; }

        string root = CheckedTag(DiskDrivePanel) ?? "";
        if (root.Length == 0) { DiskStatus.Text = "Escolha uma unidade."; return; }
        long fileMb = long.Parse(CheckedTag(DiskSizePanel) ?? "1024");

        _gameBenchCts = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
        var ct = _gameBenchCts.Token;
        SetGameBenchRunning(BtnDiskBench, true);
        DiskDrivePanel.IsEnabled = DiskSizePanel.IsEnabled = false;
        DiskResult.ItemsSource = null;
        (string Phase, double Frac) progress = ("Preparando…", 0);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            DiskStatus.Text = progress.Phase;
            DiskProgress.ScaleX = progress.Frac;
        }, Dispatcher);
        timer.Start();

        try
        {
            // Os sensores consultam o driver do disco: pausa durante o teste para não atrapalhar a medição.
            _gpuBenchActive = true;
            var r = await Task.Run(() => DiskBench.Run(root, fileMb, (phase, f) => progress = (phase, f), ct), ct);
            ct.ThrowIfCancellationRequested();

            var rows = new List<MemRow>
            {
                new("Leitura sequencial", $"{r.SeqReadMBs:N0} MiB/s", "Blocos de 1 MB. Mede copiar arquivos grandes e carregar jogos."),
                new("Escrita sequencial", $"{r.SeqWriteMBs:N0} MiB/s", "Blocos de 1 MB, direto no disco. SSDs com cache SLC caem depois de gravar dezenas de GB."),
                new("Leitura 4 KB aleatória (1 por vez)", $"{r.ReadIopsQ1:N0} IOPS", $"Latência média de {r.ReadLatencyUs:0.0} µs. É o que define a sensação de rapidez do sistema e dos programas."),
                new("Escrita 4 KB aleatória (1 por vez)", $"{r.WriteIopsQ1:N0} IOPS", $"Latência média de {r.WriteLatencyUs:0.0} µs."),
                new("Leitura 4 KB aleatória (8 em paralelo)", $"{r.ReadIopsQ8:N0} IOPS", "Várias leituras ao mesmo tempo, como ao abrir muitos programas."),
            };
            DiskResult.ItemsSource = rows;
            DiskStatus.Text = $"Concluído em {r.Drive} · arquivo de teste ({r.FileMb} MB) apagado";
            SaveBenchRecord("disk", r.Score,
                $"{r.Drive} · {r.SeqReadMBs:N0}/{r.SeqWriteMBs:N0} MiB/s · 4K {r.ReadIopsQ1 / 1000:0.#}k IOPS");
        }
        catch (OperationCanceledException) { DiskStatus.Text = "Cancelado (arquivo de teste apagado)"; }
        catch (Exception ex)
        {
            var inner = (ex as AggregateException)?.InnerException ?? ex;
            DiskStatus.Text = $"Falhou: {inner.Message}";
        }
        finally
        {
            _gpuBenchActive = false;
            timer.Stop();
            DiskProgress.ScaleX = 0;
            DiskDrivePanel.IsEnabled = DiskSizePanel.IsEnabled = true;
            SetGameBenchRunning(BtnDiskBench, false);
        }
    }

}
