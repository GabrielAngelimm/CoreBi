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

    /// <summary>Placa do disco: a unidade escolhida, o tamanho e o espaço livre.</summary>
    internal void UpdateDiskPlate()
    {
        var chip = DiskDrivePanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (chip?.Tag is not string root) { DiskDevice.Text = "Unidade"; DiskDeviceSpec.Text = ""; return; }
        DiskDevice.Text = $"Unidade {chip.Content}";
        try
        {
            var d = new System.IO.DriveInfo(root);
            DiskDeviceSpec.Text = $"{d.TotalSize / 1e9:N0} GB, {d.TotalFreeSpace / 1e9:N0} GB livres";
        }
        catch { DiskDeviceSpec.Text = ""; }
    }

    /// <summary>As cinco medições, com travessão antes do teste (a tabela já nasce com a altura final).</summary>
    internal static List<MemRow> DiskRows(DiskBenchResult? r)
    {
        string N(double? v) => v is double d ? $"{d:N0}" : "—";
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

    internal async void BtnDiskBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "Disk IO") { _gameBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { DiskStatus.Text = "Termine o outro teste antes."; DiskDetail.Text = ""; return; }
        using var workload = _workloads.TryBegin("Disk IO");
        if (workload == null) { DiskStatus.Text = "Termine o outro teste antes."; DiskDetail.Text = ""; return; }

        string root = CheckedTag(DiskDrivePanel) ?? "";
        if (root.Length == 0) { DiskStatus.Text = "Escolha uma unidade."; DiskDetail.Text = ""; return; }
        long fileMb = long.Parse(CheckedTag(DiskSizePanel) ?? "1024");

        _gameBenchCts = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
        var ct = _gameBenchCts.Token;
        SetGameBenchRunning(BtnDiskBench, true);
        DiskDrivePanel.IsEnabled = DiskSizePanel.IsEnabled = false;
        DiskResult.ItemsSource = DiskRows(null);
        (string Phase, double Frac) progress = ("Preparando", 0);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            DiskStatus.Text = progress.Phase;
            DiskDetail.Text = $"{progress.Frac:P0}";
            DiskProgress.ScaleX = progress.Frac;
        }, Dispatcher);
        timer.Start();

        try
        {
            // Os sensores consultam o driver do disco: pausa durante o teste para não atrapalhar a medição.
            _gpuBenchActive = true;
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
            _gpuBenchActive = false;
            timer.Stop();
            DiskProgress.ScaleX = 0;
            DiskDrivePanel.IsEnabled = DiskSizePanel.IsEnabled = true;
            SetGameBenchRunning(BtnDiskBench, false);
        }
    }

}
