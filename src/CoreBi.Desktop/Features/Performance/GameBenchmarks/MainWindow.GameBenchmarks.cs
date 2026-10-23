using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Engine.Performance.Benchmarks;
using CoreBi.Engine.Persistence;

using System.Windows;
using System.Windows.Controls;

using System.Windows.Media;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    // ---------------- Benchmark: benchmarks de jogo ----------------

    public sealed record MemRow(string Label, string Value, string Hint);
    internal bool _gameBenchRunning;
    internal GpuGameBench? _gpuBench;
    internal bool _gpuBenchActive;
    internal CancellationTokenSource? _gameBenchCts;
    internal System.Windows.Media.Imaging.WriteableBitmap? _simBitmap;
    internal int[] _simPixels = Array.Empty<int>();
    internal const int SimView = 440;

    internal bool AnyBenchRunning => _workloads.IsBusy || BenchRunning || _gameBenchRunning;

    internal void BenchTest_Checked(object sender, RoutedEventArgs e)
    {
        if (BenchPanelRender == null) return; // durante InitializeComponent
        string test = (string)((FrameworkElement)sender).Tag;
        BenchPanelRender.Visibility = test == "render" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelGpu.Visibility = test == "gpu" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelRt.Visibility = test == "rt" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelSim.Visibility = test == "sim" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelMem.Visibility = test == "mem" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelDisk.Visibility = test == "disk" ? Visibility.Visible : Visibility.Collapsed;
        if (test == "disk") InitDiskBench();
        BenchSub.Text = test switch
        {
            "gpu" => "Cena de jogo em Direct3D 11 para medir a placa de vídeo, no estilo 3DMark e Unigine.",
            "rt" => "Ray tracing por hardware (DXR 1.1, Direct3D 12) nos núcleos de RT da placa, com 3 passadas e nota pela mediana.",
            "sim" => "Quanto FPS o processador aguenta numa simulação de jogo com milhares de unidades.",
            "mem" => "Latência e banda da memória RAM e velocidade de carregamento do disco.",
            "disk" => "Leitura e escrita do disco, sequenciais e aleatórias de 4 KB, no estilo CrystalDiskMark.",
            _ => "Renderização 3D por path tracing no processador, no mesmo princípio do Cinebench.",
        };
        UpdateGameBenchHistory();
    }

    internal void SaveBenchRecord(string test, double score, string summary)
    {
        AddEvent("", "Benchmark concluído", summary, "");
        _settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, test, score, summary));
        // Mantém os 10 mais recentes de cada teste.
        _settings.BenchRecords = _settings.BenchRecords.GroupBy(r => r.Test).SelectMany(g => g.Take(10))
                                          .OrderByDescending(r => r.Date).ToList();
        SaveSettings();
        UpdateGameBenchHistory();
    }

    internal void UpdateGameBenchHistory()
    {
        List<MeterItem> For(string test)
        {
            var list = _settings.BenchRecords.Where(r => r.Test == test).ToList();
            double best = list.Count > 0 ? list.Max(r => r.Score) : 1;
            return list.Select(r => new MeterItem($"{r.Date:dd/MM HH:mm}", r.Summary, best > 0 ? r.Score / best : 0)).ToList();
        }
        GbHistory.ItemsSource = For("gpu");
        RtHistory.ItemsSource = RtHistoryItems();
        SimHistory.ItemsSource = For("sim");
        MemHistory.ItemsSource = For("mem");
        DiskHistory.ItemsSource = For("disk");
    }

    internal void SetGameBenchRunning(Button btn, bool running)
    {
        _gameBenchRunning = running;
        Ui.SetRunning(btn, running, "Iniciar", "Cancelar");
        BenchTestPanel.IsEnabled = !running;
    }

    // ----- Cena de jogo (GPU) -----

    internal async void BtnGpuBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "GPU raster") { _gpuBench?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { GbStatus.Text = "Termine o outro teste antes."; return; }
        using var workload = _workloads.TryBegin("GPU raster");
        if (workload == null) { GbStatus.Text = "Termine o outro teste antes."; return; }

        var size = (CheckedTag(GbResPanel) ?? "1920x1080").Split('x').Select(int.Parse).ToArray();
        int secs = int.Parse(CheckedTag(GbDurPanel) ?? "60");
        _gpuBench = new GpuGameBench
        {
            Width = size[0],
            Height = size[1],
            Duration = TimeSpan.FromSeconds(secs),
            AdapterName = _gpus.Count > 0 ? _gpus[0].Name : ""
        };
        SetGameBenchRunning(BtnGpuBench, true);
        GbResult.ItemsSource = null;
        var bench = _gpuBench;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            GbFpsNow.Text = bench.CurrentFps > 0 ? $"{bench.CurrentFps:0}" : "—";
            GbProgress.ScaleX = bench.Progress;
            GbStatus.Text = $"Rodando · {size[0]}x{size[1]} · {bench.Progress:P0}";
        }, Dispatcher);
        timer.Start();
        _gpuBenchActive = true;
        GbGpuNow.Text = "sensores pausados";
        bool overlayWasOn = ChkOverlay.IsChecked == true;
        if (overlayWasOn) ChkOverlay.IsChecked = false; // o overlay também consulta a GPU

        // Thread dedicada: a cena roda num loop próprio com prioridade alta.
        GpuGameResult? r = null;
        try
        {
            r = await Task.Factory.StartNew(bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception ex) { GbStatus.Text = "Falhou: " + ex.Message; return; }
        finally
        {
            timer.Stop();
            _gpuBenchActive = false;
            if (overlayWasOn) ChkOverlay.IsChecked = true;
            GbGpuNow.Text = "—";
            SetGameBenchRunning(BtnGpuBench, false);
            GbProgress.ScaleX = 0;
            GbFpsNow.Text = "—";
        }

        if (r == null)
        {
            GbStatus.Text = bench.Cancelled ? "Cancelado" : $"Falhou: {bench.Error}";
            return;
        }
        GbStatus.Text = $"Concluído · {r.Adapter}";
        GbResult.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("Nota", $"{r.Score:N0} pts"),
            new KeyValuePair<string, string>("FPS médio", $"{r.AvgFps:0.0}"),
            new KeyValuePair<string, string>("1% low", $"{r.Low1Fps:0.0}"),
            new KeyValuePair<string, string>("Resolução", $"{r.Width}x{r.Height}"),
            new KeyValuePair<string, string>("Frames", $"{r.Frames:N0}"),
        };
        SaveBenchRecord("gpu", r.Score, $"{r.Score:N0} pts · {r.AvgFps:0} FPS · {r.Height}p");
    }

    // ----- Simulação de jogo (CPU) -----

    internal async void BtnSimBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "CPU simulation") { _gameBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { SimStatus.Text = "Termine o outro teste antes."; return; }
        using var workload = _workloads.TryBegin("CPU simulation");
        if (workload == null) { SimStatus.Text = "Termine o outro teste antes."; return; }

        int secs = int.Parse(CheckedTag(SimDurPanel) ?? "20");
        _simBitmap ??= new System.Windows.Media.Imaging.WriteableBitmap(SimView, SimView, 96, 96, PixelFormats.Bgra32, null);
        if (_simPixels.Length == 0) _simPixels = new int[SimView * SimView];
        SimImage.Source = _simBitmap;
        SimIdle.Visibility = Visibility.Collapsed;

        var sim = new GameSimBench();
        _gameBenchCts = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
        var ct = _gameBenchCts.Token;
        SetGameBenchRunning(BtnSimBench, true);
        SimResult.ItemsSource = null;
        SimStatus.Text = "Aquecendo (2 s)…";
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) =>
        {
            DrawSim(sim);
            SimProgress.ScaleX = sim.Progress;
            if (sim.FramesDone > 0) SimStatus.Text = $"Simulando · {sim.Progress:P0}";
            SimFpsNow.Text = sim.CurrentFps > 0 ? $"{sim.CurrentFps:0} FPS" : "";
        }, Dispatcher);
        timer.Start();

        GameSimResult r;
        try
        {
            r = await Task.Run(() => sim.Run(TimeSpan.FromSeconds(secs), ct));
            if (ct.IsCancellationRequested) { SimStatus.Text = "Cancelado"; return; }
        }
        catch (OperationCanceledException) { SimStatus.Text = "Cancelado"; return; }
        catch (Exception ex) { SimStatus.Text = "Falhou: " + ex.Message; return; }
        finally
        {
            timer.Stop();
            SetGameBenchRunning(BtnSimBench, false);
            SimProgress.ScaleX = 0;
            SimFpsNow.Text = "";
            _gameBenchCts?.Dispose();
            _gameBenchCts = null;
        }

        SimStatus.Text = $"Concluído · {r.Frames:N0} frames em {r.Seconds:0} s";
        SimResult.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("FPS médio (nota)", $"{r.AvgFps:0}"),
            new KeyValuePair<string, string>("1% low", $"{r.Low1Fps:0}"),
            new KeyValuePair<string, string>("Thread principal", $"{r.MainThreadMs:0.00} ms/frame"),
            new KeyValuePair<string, string>("Física + IA (paralelo)", $"{r.ParallelMs:0.00} ms/frame"),
            new KeyValuePair<string, string>("Unidades", $"{GameSimBench.Entities:N0}"),
        };
        SaveBenchRecord("sim", r.Score, $"{r.AvgFps:0} FPS · 1% low {r.Low1Fps:0}");
    }

    /// <summary>Vista de cima da batalha: time A em branco, time B em cinza.</summary>
    internal void DrawSim(GameSimBench sim)
    {
        Array.Fill(_simPixels, unchecked((int)0xFF050505));
        float[] xs = sim.ViewX, ys = sim.ViewY; byte[] team = sim.ViewTeam;
        float k = SimView / GameSimBench.World;
        for (int i = 0; i < GameSimBench.Entities; i++)
        {
            int px = (int)(xs[i] * k), py = (int)(ys[i] * k);
            if ((uint)px < SimView && (uint)py < SimView)
                _simPixels[py * SimView + px] = team[i] == 0 ? unchecked((int)0xFFF5F5F5) : unchecked((int)0xFF5A5A5A);
        }
        _simBitmap!.WritePixels(new Int32Rect(0, 0, SimView, SimView), _simPixels, SimView * 4, 0);
    }

    // ----- Memória e carregamento -----

    internal async void BtnMemBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "RAM and loading") { _gameBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { MemStatus.Text = "Termine o outro teste antes."; return; }
        using var workload = _workloads.TryBegin("RAM and loading");
        if (workload == null) { MemStatus.Text = "Termine o outro teste antes."; return; }

        _gameBenchCts = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
        var ct = _gameBenchCts.Token;
        SetGameBenchRunning(BtnMemBench, true);
        var rows = new List<MemRow>();
        void Show() => MemResult.ItemsSource = rows.ToList();
        (string Phase, double Frac) progress = ("", 0);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            MemStatus.Text = progress.Phase;
            MemProgress.ScaleX = progress.Frac;
        }, Dispatcher);
        timer.Start();

        try
        {
            progress = ("Medindo latência da RAM…", 0.05);
            double lat = await Task.Run(() => MemoryBench.MeasureLatencyNs(ct));
            ct.ThrowIfCancellationRequested();
            rows.Add(new MemRow("Latência da RAM", $"{lat:0.0} ns", "Acesso aleatório. Menor é melhor; pesa muito em jogos competitivos."));
            Show();

            progress = ("Medindo banda da RAM…", 0.2);
            var bw = await Task.Run(() => MemoryBench.MeasureBandwidth(ct));
            ct.ThrowIfCancellationRequested();
            rows.Add(new MemRow("Banda de leitura da RAM", $"{bw.Read:0.0} GB/s", "Todas as threads lendo em sequência."));
            rows.Add(new MemRow("Banda de cópia da RAM", $"{bw.Copy:0.0} GB/s", "Leitura + escrita."));
            Show();

            var load = await Task.Run(() => MemoryBench.MeasureLoading((phase, f) => progress = (phase, 0.35 + f * 0.6), ct));
            rows.Add(new MemRow($"SSD {load.Drive} · leitura sequencial", $"{load.SeqReadMBs:N0} MiB/s", "Pacote de 512 MB, sem cache do Windows."));
            rows.Add(new MemRow("Assets pequenos", $"{load.FilesPerSec:N0} arq/s",
                                $"Mil arquivos de 64 KB com 8 threads ({load.SmallFilesMBs:N0} MiB/s), como engines modernas."));
            rows.Add(new MemRow("Carregamento do mapa", $"{load.MapLoadSeconds:0.00} s", "Pacote + assets: quanto menor, mais rápido o jogo carrega."));
            Show();

            MemStatus.Text = "Concluído. Os arquivos de teste foram apagados.";
            // Nota para a barra do histórico: mais rápido = maior (inverso da latência × velocidade de carga).
            SaveBenchRecord("mem", 1000 / lat + 10 / load.MapLoadSeconds,
                            $"{lat:0} ns · {bw.Read:0} GB/s · mapa {load.MapLoadSeconds:0.00} s");
        }
        catch (OperationCanceledException) { MemStatus.Text = "Cancelado (arquivos de teste apagados)"; }
        catch (Exception ex) { MemStatus.Text = $"Falhou: {ex.Message}"; }
        finally
        {
            timer.Stop();
            MemProgress.ScaleX = 0;
            SetGameBenchRunning(BtnMemBench, false);
        }
    }

}
