using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Performance.Benchmarks;

namespace CoreBi.Desktop.Views;

public partial class BenchmarksView
{
    private const int SimView = 440;

    private GpuGameBench? _gpuBench;
    private GpuRtBench? _rtBench;
    private string? _rtSupport;
    private bool _rtChecking;
    private WriteableBitmap? _simBitmap;
    private int[] _simPixels = Array.Empty<int>();

    private static List<BenchStat> GpuStats(GpuGameResult? r, string vs = "") => new()
    {
        new("Nota", r is null ? "—" : $"{r.Score:N0}", "pts", vs),
        new("FPS médio", r is null ? "—" : $"{r.AvgFps:0.0}", "FPS", "Quadros por segundo"),
        new("1% low", r is null ? "—" : $"{r.Low1Fps:0.0}", "FPS", "Quadros mais lentos", Tip: "Média do 1% de quadros mais lentos: mostra os engasgos que o FPS médio esconde."),
        new("Quadros", r is null ? "—" : $"{r.Frames:N0}"),
        new("Resolução", r is null ? "—" : $"{r.Width} × {r.Height}"),
    };

    private async void BtnGpuBench_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null) return;
        if (_context.Workloads.ActiveOperation == WorkloadOperations.GpuRaster) { _gpuBench?.Cancel(); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.GpuRaster);
        if (workload == null) { GbStatus.Text = "Termine o outro teste antes."; GbDetail.Text = ""; return; }

        var size = (Ui.CheckedTag(GbResPanel) ?? "1920x1080").Split('x').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        int secs = int.Parse(Ui.CheckedTag(GbDurPanel) ?? "60", CultureInfo.InvariantCulture);
        _gpuBench = new GpuGameBench
        {
            Width = size[0],
            Height = size[1],
            Duration = TimeSpan.FromSeconds(secs),
            AdapterName = _context.PrimaryGpu?.Name ?? "",
        };
        SetGameBenchRunning(BtnGpuBench, true);
        GbResult.ItemsSource = GpuStats(null);
        GbTrace.Clear();
        GbStatus.Text = $"Rodando em {size[0]} × {size[1]}";
        GbDetail.Text = "";
        var bench = _gpuBench;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            GbFpsNow.Text = bench.CurrentFps > 0 ? $"{bench.CurrentFps:0}" : "—";
            GbProgress.ScaleX = bench.Progress;
            GbTrace.Add(bench.Progress, bench.CurrentFps);
            GbDetail.Text = $"{bench.Progress * 100:0} %";
        }, Dispatcher);
        timer.Start();
        _context.SetBenchmarkIsolation(true, true);
        GbGpuNow.Text = "Sensores pausados durante o teste";

        GpuGameResult? r = null;
        try
        {
            r = await Task.Factory.StartNew(bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception ex) { GbStatus.Text = "Falhou: " + ex.Message; GbDetail.Text = ""; return; }
        finally
        {
            timer.Stop();
            _context.SetBenchmarkIsolation(false, false);
            GbGpuNow.Text = "";
            SetGameBenchRunning(BtnGpuBench, false);
            Ui.Rewind(GbProgress);
            GbFpsNow.Text = "—";
        }

        if (r == null)
        {
            GbStatus.Text = bench.Cancelled ? "" : $"Falhou: {bench.Error}";
            GbDetail.Text = "";
            return;
        }
        string vs = VsBest(r.Score, _context.Settings.BenchRecords
            .Where(x => x.Test == "gpu" && SummaryParts(x.Summary).LastOrDefault() == $"{r.Height}p").Select(x => x.Score));
        GbStatus.Text = $"Concluído em {TimeSpan.FromSeconds(r.Seconds):m\\:ss}";
        GbDetail.Text = "";
        GbResult.ItemsSource = GpuStats(r, vs);
        SaveBenchRecord("gpu", r.Score, $"{r.Score:N0} pts · {r.AvgFps:0} FPS · {r.Height}p", GpuStats(r).Select(Figure), GbTrace.Snapshot());
    }

    private async void CheckRtSupportOnce()
    {
        if (_context == null || _rtSupport != null || _rtChecking || _context.Workloads.IsBusy) return;
        _rtChecking = true;
        string adapterName = _context.PrimaryGpu?.Name ?? "";
        string? why = await Task.Run(() => GpuRtBench.CheckSupport(adapterName));
        _rtChecking = false;
        if (_context.IsClosing) return;
        _rtSupport = why ?? "";
        if (why == null) return;

        var (status, detail) = RtSupportText(why);
        if (BenchTestPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.Tag as string == "rt") is { } tab)
        {
            Ui.SetCaption(tab, "Sem suporte nesta placa");
            tab.ToolTip = $"{tab.ToolTip}\n\n{why}";
        }
        RtStatus.Text = status;
        RtDetail.Text = detail;
        RtDetail.ToolTip = detail.Length > 0 ? detail : null;
        BtnRtBench.IsEnabled = false;
        BtnRtBench.ToolTip = why;
    }

    private static (string Status, string Detail) RtSupportText(string why)
    {
        if (why.StartsWith("Direct3D 12 indisponível", StringComparison.Ordinal)) return ("Sem Direct3D 12 nesta placa", "");
        int dot = why.IndexOf(". ", StringComparison.Ordinal);
        return dot < 0 ? (why.TrimEnd('.'), "") : (why[..dot], why[(dot + 2)..]);
    }

    private static string PresetName(string p) => p switch { "normal" => "Normal", "extrema" => "Extrema", _ => "Alta" };

    private static List<BenchStat> RtStats(GpuRtResult? r, string vs = "")
    {
        string passes = r is null ? "" : string.Join("\n", r.Passes.Select((p, i) => $"Passada {i + 1}: {p.AvgFps:0.0} FPS, 1% low {p.Low1Fps:0.0}"));
        string reliability = r is null ? ""
            : r.Reliable ? "\nAbaixo de 3 %: as passadas concordam e o resultado serve para comparar."
            : "\nAcima de 3 %: algo atrapalhou (programa em segundo plano, temperatura ou limite de energia). Feche outros programas e rode de novo antes de comparar.";
        return new()
        {
            new("Nota", r is null ? "—" : $"{r.Score:N0}", "pts", vs,
                Tip: "Referência medida: AMD Radeon RX 9070, preset Alta em 1440p, modo Padrão: 878 pts (87,8 FPS, 1% low 77), variação de 0,5 %."),
            new("FPS, mediana", r is null ? "—" : $"{r.MedianFps:0.0}", "FPS", "Quadros por segundo"),
            new("1% low, mediana", r is null ? "—" : $"{r.MedianLow1:0.0}", "FPS", "Quadros mais lentos",
                Tip: "Média do 1% de quadros mais lentos: mostra os engasgos que o FPS médio esconde."),
            new("Variação entre passadas", r is null ? "—" : $"{r.VariationPct:0.0}", "%",
                r is null ? "" : r.Reliable ? "Confiável" : "Instável, rode de novo", r is null ? "" : r.Reliable ? "Ok" : "Warn",
                r is null ? null : passes + reliability),
            new("Raios por segundo", r is null ? "—" : $"{r.GigaRaysPerSec:0.0}", "bilhões", r is null ? "" : "estimado",
                Tip: "Estimado pelas amostras e rebatidas por pixel do preset."),
        };
    }

    private async void BtnRtBench_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null) return;
        if (_context.Workloads.ActiveOperation == WorkloadOperations.GpuRayTracing) { _rtBench?.Cancel(); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.GpuRayTracing);
        if (workload == null) { RtStatus.Text = "Termine o outro teste antes."; RtDetail.Text = ""; return; }

        string adapterName = _context.PrimaryGpu?.Name ?? "";
        RtStatus.Text = "Verificando suporte a ray tracing…";
        RtDetail.Text = "";
        string? why = await Task.Run(() => GpuRtBench.CheckSupport(adapterName));
        if (why != null) { (RtStatus.Text, RtDetail.Text) = RtSupportText(why); return; }

        var size = (Ui.CheckedTag(RtResPanel) ?? "2560x1440").Split('x').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        string preset = Ui.CheckedTag(RtPresetPanel) ?? "alta";
        bool quick = Ui.CheckedTag(RtModePanel) == "quick";
        _rtBench = new GpuRtBench
        {
            Width = size[0],
            Height = size[1],
            Preset = preset,
            AdapterName = adapterName,
            Passes = 3,
            Warmup = TimeSpan.FromSeconds(quick ? 5 : 20),
            PassDuration = TimeSpan.FromSeconds(quick ? 10 : 30),
        };
        var bench = _rtBench;
        SetGameBenchRunning(BtnRtBench, true);
        RtResult.ItemsSource = RtStats(null);
        RtTrace.Clear();
        RtStatus.Text = "Rodando";
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            RtFpsNow.Text = bench.CurrentFps > 0 ? $"{bench.CurrentFps:0}" : "—";
            RtProgress.ScaleX = bench.Progress;
            RtTrace.Add(bench.Progress, bench.CurrentFps);
            RtStatus.Text = bench.Phase.Length > 0 ? bench.Phase : "Rodando";
            RtDetail.Text = $"{bench.Progress * 100:0} %";
        }, Dispatcher);
        timer.Start();

        _context.SetBenchmarkIsolation(true, true);
        GpuRtResult? r = null;
        try
        {
            r = await Task.Factory.StartNew(bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception ex) { RtStatus.Text = "Falhou: " + ex.Message; RtDetail.Text = ""; return; }
        finally
        {
            timer.Stop();
            _context.SetBenchmarkIsolation(false, false);
            SetGameBenchRunning(BtnRtBench, false);
            Ui.Rewind(RtProgress);
            RtFpsNow.Text = "—";
        }

        if (r == null)
        {
            RtStatus.Text = bench.Cancelled ? "" : $"Falhou: {bench.Error}";
            RtDetail.Text = "";
            return;
        }
        string key = $"rt:{r.Preset}:{r.Height}";
        string vs = VsBest(r.Score, _context.Settings.BenchRecords.Where(x => x.Test == key).Select(x => x.Score));
        RtStatus.Text = "Concluído";
        RtDetail.Text = "";
        RtResult.ItemsSource = RtStats(r, vs);
        SaveBenchRecord(key, r.Score, $"{r.Score:N0} pts · {r.MedianFps:0} FPS · {PresetName(r.Preset)} {r.Height}p" + (r.Reliable ? "" : " · instável"),
            RtStats(r).Select(Figure), RtTrace.Snapshot());
    }

    private static List<BenchStat> SimStats(GameSimResult? r, string vs = "") => new()
    {
        new("FPS médio", r is null ? "—" : $"{r.AvgFps:0}", "FPS", vs, Tip: "A nota do teste é o FPS médio."),
        new("1% low", r is null ? "—" : $"{r.Low1Fps:0}", "FPS", "Quadros mais lentos", Tip: "Média do 1% de quadros mais lentos."),
        new("Thread principal", r is null ? "—" : $"{r.MainThreadMs:0.00}", "ms/quadro", Tip: "Grade espacial, lógica e preparo do desenho, numa thread só."),
        new("Física e IA", r is null ? "—" : $"{r.ParallelMs:0.00}", "ms/quadro", Tip: "Física, colisão e IA das unidades, em paralelo nas outras threads."),
    };

    private async void BtnSimBench_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null) return;
        if (_context.Workloads.ActiveOperation == WorkloadOperations.CpuSimulation) { _gameBenchCts?.Cancel(); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.CpuSimulation);
        if (workload == null) { SimStatus.Text = "Termine o outro teste antes."; SimDetail.Text = ""; return; }

        int secs = int.Parse(Ui.CheckedTag(SimDurPanel) ?? "20", CultureInfo.InvariantCulture);
        _simBitmap ??= new WriteableBitmap(SimView, SimView, 96, 96, PixelFormats.Bgra32, null);
        if (_simPixels.Length == 0) _simPixels = new int[SimView * SimView];
        SimImage.Source = _simBitmap;
        SimImage.Opacity = 1;
        SimIdle.Visibility = Visibility.Collapsed;

        var sim = new GameSimBench();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        _gameBenchCts = cts;
        var ct = cts.Token;
        SetGameBenchRunning(BtnSimBench, true);
        SimResult.ItemsSource = SimStats(null);
        SimStatus.Text = "Aquecendo";
        SimDetail.Text = "";
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) =>
        {
            DrawSim(sim);
            SimProgress.ScaleX = sim.Progress;
            if (sim.FramesDone > 0) { SimStatus.Text = "Simulando"; SimDetail.Text = $"{sim.Progress * 100:0} %"; }
            SimFpsNow.Text = sim.CurrentFps > 0 ? $"{sim.CurrentFps:0}" : "—";
        }, Dispatcher);
        timer.Start();

        GameSimResult r;
        try
        {
            r = await Task.Run(() => sim.Run(TimeSpan.FromSeconds(secs), ct), ct);
            if (ct.IsCancellationRequested) { SimStatus.Text = ""; SimDetail.Text = ""; return; }
        }
        catch (OperationCanceledException) { SimStatus.Text = ""; SimDetail.Text = ""; return; }
        catch (Exception ex) { SimStatus.Text = "Falhou: " + ex.Message; SimDetail.Text = ""; return; }
        finally
        {
            timer.Stop();
            SetGameBenchRunning(BtnSimBench, false);
            Ui.Rewind(SimProgress);
            SimFpsNow.Text = "—";
            _gameBenchCts = null;
        }

        string vs = VsBest(r.Score, _context.Settings.BenchRecords.Where(x => x.Test == "sim").Select(x => x.Score));
        SimStatus.Text = $"Concluído em {TimeSpan.FromSeconds(r.Seconds):m\\:ss}";
        SimDetail.Text = "";
        SimResult.ItemsSource = SimStats(r, vs);
        SaveBenchRecord("sim", r.Score, $"{r.AvgFps:0} FPS · 1% low {r.Low1Fps:0}", SimStats(r).Select(Figure));
    }

    /// <summary>O palco da simulação parado: a formação inicial do teste (a mesma de toda passada), esmaecida.</summary>
    private void InitSimStage()
    {
        _simBitmap ??= new WriteableBitmap(SimView, SimView, 96, 96, PixelFormats.Bgra32, null);
        if (_simPixels.Length == 0) _simPixels = new int[SimView * SimView];
        SimImage.Source = _simBitmap;
        var sim = new GameSimBench();
        sim.Prepare();
        DrawSim(sim);
    }

    private void DrawSim(GameSimBench sim)
    {
        if (_simBitmap == null) return;
        Array.Fill(_simPixels, unchecked((int)0xFF050505));
        float[] xs = sim.ViewX, ys = sim.ViewY;
        byte[] team = sim.ViewTeam;
        float k = SimView / GameSimBench.World;
        for (int i = 0; i < GameSimBench.Entities; i++)
        {
            int px = (int)(xs[i] * k), py = (int)(ys[i] * k);
            if ((uint)px < SimView && (uint)py < SimView)
                _simPixels[py * SimView + px] = team[i] == 0 ? unchecked((int)0xFFF5F5F5) : unchecked((int)0xFF5A5A5A);
        }
        _simBitmap.WritePixels(new Int32Rect(0, 0, SimView, SimView), _simPixels, SimView * 4, 0);
    }
}
