using System.Windows.Threading;
using CoreBi.Engine.Games;
using CoreBi.Engine.Performance.Benchmarks;
using CoreBi.Engine.Persistence;

using System.Windows;
using System.Windows.Controls;

using System.Windows.Media;
using System.Windows.Shapes;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    // Benchmark
    internal readonly BenchRenderer _bench = new();
    internal System.Windows.Media.Imaging.WriteableBitmap? _benchBitmap;
    internal CancellationTokenSource? _benchCts;
    internal DispatcherTimer? _benchTimer;
    internal DateTime _benchStart;
    internal string _benchPhase = "";
    internal DateTime _benchPhaseStart;

    // ---------------- Benchmark ----------------

    internal void InitBench()
    {
        _benchBitmap = new System.Windows.Media.Imaging.WriteableBitmap(BenchRenderer.Width, BenchRenderer.Height, 96, 96,
                                                                       PixelFormats.Bgra32, null);
        BenchImage.Source = _benchBitmap;
        ResetBenchStages();
        UpdateBenchHistory();
        UpdateGameBenchHistory();
        UpdateBenchLive();
    }

    internal bool BenchRunning => _benchCts != null;

    internal async void BtnBench_Click(object sender, RoutedEventArgs e)
    {
        if (BenchRunning) { _benchCts!.Cancel(); return; }
        if (TestRunning || _gameBenchRunning) { BenchHint.Text = "Termine o outro teste antes."; return; }
        using var workload = _workloads.TryBegin("CPU render");
        if (workload == null) { BenchHint.Text = "Termine o outro teste antes."; return; }

        string mode = CheckedTag(BenchModePanel) ?? "both";
        bool sustained = CheckedTag(BenchDurationPanel) == "10";
        var minDuration = sustained ? TimeSpan.FromMinutes(10) : TimeSpan.Zero;
        int threads = Environment.ProcessorCount;

        _benchCts = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
        var ct = _benchCts.Token;
        Ui.SetRunning(BtnBench, true, "Iniciar teste", "Cancelar");
        PageBench.BenchOptions.IsEnabled = BenchTestPanel.IsEnabled = false;
        BenchHint.Text = "";
        BenchIdle.Visibility = Visibility.Collapsed;
        BenchMt.Text = BenchSt.Text = BenchRatio.Text = "—";
        BenchMtVs.Text = BenchStVs.Text = BenchRatioSub.Text = "";
        _benchStart = DateTime.Now;
        _benchTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => UpdateBenchView(), Dispatcher);
        _benchTimer.Start();

        double? mt = null, st = null;
        try
        {
            if (mode is "mt" or "both")
            {
                _benchPhase = "Multi-core";
                _benchPhaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(threads, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { mt = r.Score; BenchMt.Text = $"{r.Score:N0}"; }
            }
            if (!ct.IsCancellationRequested && mode is "st" or "both")
            {
                _benchPhase = "Single-core";
                _benchPhaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(1, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { st = r.Score; BenchSt.Text = $"{r.Score:N0}"; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BenchHint.Text = "Falhou: " + ex.Message; _benchCts?.Cancel(); }

        _benchTimer.Stop();
        UpdateBenchView();
        bool cancelled = ct.IsCancellationRequested;
        _benchCts = null;
        Ui.SetRunning(BtnBench, false, "Iniciar teste", "Cancelar");
        PageBench.BenchOptions.IsEnabled = BenchTestPanel.IsEnabled = true;
        BenchBuckets.Children.Clear();
        BenchElapsed.Text = "";

        var total = DateTime.Now - _benchStart;
        if (cancelled)
        {
            BenchStatus.Text = "Cancelado";
            return;
        }
        BenchStatus.Text = $"Concluído em {total:m\\:ss}";
        if (mt is double m && st is double s)
        {
            BenchRatio.Text = $"{m / s:0.0}";
            BenchRatioSub.Text = $"com {threads} threads";
        }
        // Só compara com passadas da mesma duração: a de 10 min aquece o processador e rende menos.
        var previous = _settings.BenchRuns.Where(r => r.Sustained == sustained).ToList();
        if (mt is double mv) BenchMtVs.Text = VsBest(mv, previous.Select(r => r.Multi ?? 0));
        if (st is double sv) BenchStVs.Text = VsBest(sv, previous.Select(r => r.Single ?? 0));

        _settings.BenchRuns.Insert(0, new BenchRun(DateTime.Now, mt, st, sustained, HardwareCatalog.ShortCpuName(_cpuName)));
        AddEvent("cpu", "Benchmark", "Render de CPU", mt is double mte ? $"{mte:N0} pts" : st is double ste ? $"{ste:N0} pts" : "", "Cpu");
        if (_settings.BenchRuns.Count > 10) _settings.BenchRuns.RemoveRange(10, _settings.BenchRuns.Count - 10);
        SaveSettings();
        UpdateBenchHistory();
    }

    /// <summary>Copia a imagem parcial e desenha o contorno dos blocos que cada thread está renderizando.</summary>
    internal void UpdateBenchView()
    {
        _benchBitmap!.WritePixels(new Int32Rect(0, 0, BenchRenderer.Width, BenchRenderer.Height),
                                  _bench.Pixels, BenchRenderer.Width * 4, 0);

        BenchBuckets.Children.Clear();
        var active = _bench.Active;
        foreach (int b in active)
        {
            if (b < 0) continue;
            var (x, y, w, h) = _bench.Buckets[b];
            var rect = new Rectangle { Width = w, Height = h, Stroke = Palette("Signal"), StrokeThickness = 2 };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            BenchBuckets.Children.Add(rect);
        }

        if (BenchRunning)
        {
            double frac = _bench.BucketsTotal > 0 ? (double)_bench.BucketsDone / _bench.BucketsTotal : 0;
            BenchProgress.ScaleX = frac;
            // Tempo restante da passada atual, pela velocidade até agora (só depois de 3% para não oscilar).
            var phaseElapsed = DateTime.Now - _benchPhaseStart;
            string eta = frac >= 0.03 ? $", faltam cerca de {TimeSpan.FromSeconds(phaseElapsed.TotalSeconds * (1 - frac) / frac):m\\:ss}" : "";
            BenchStatus.Text = $"{_benchPhase}, {frac * 100:0} %";
            BenchElapsed.Text = (DateTime.Now - _benchStart).ToString(@"mm\:ss") + eta;
        }
        else BenchProgress.ScaleX = 0;
    }

    /// <summary>Temperatura, clock médio dos núcleos, consumo e carga do processador (atualizado junto com os sensores).
    /// Leitura que o sensor não dá não aparece.</summary>
    internal void UpdateBenchLive()
    {
        var cells = new List<BenchStat>();
        if (_monitor.CpuTemperature is float t) cells.Add(new("Temperatura", $"{t:0.#}", "°C"));
        if (CpuCoreClock() is double clock) cells.Add(new("Clock médio", $"{clock:0}", "MHz", Tip: "Média dos núcleos"));
        if (_monitor.CpuPower is float p) cells.Add(new("Consumo", $"{p:0.#}", "W"));
        if (_monitor.CpuLoad is float l) cells.Add(new("Carga", $"{l:0}", "%"));
        BenchLive.ItemsSource = cells;
        UpdateBenchPlates();
    }

    internal void UpdateBenchHistory()
    {
        var runs = _settings.BenchRuns;
        // A barra compara com o melhor resultado da mesma duração (uma passada ou 10 min).
        double Best(bool sustained, bool multi) => runs.Where(x => x.Sustained == sustained).Max(x => multi ? x.Multi : x.Single) ?? 0;
        BenchHistory.ItemsSource = runs.Select(r =>
        {
            var detail = new List<string>();
            if (r.Multi != null) detail.Add(r.Single is double s ? $"single-core {s:N0}" : "só multi-core");
            else detail.Add("só single-core");
            if (r.Sustained) detail.Add("10 min");
            string value = r.Multi is double m ? $"{m:N0} pts" : r.Single is double ss ? $"{ss:N0} pts" : "—";
            double best = Best(r.Sustained, r.Multi != null);
            double frac = best <= 0 ? 0 : r.Multi is double mm ? mm / best : r.Single is double s1 ? s1 / best : 0;
            return new BenchHistoryRow($"{r.Date:dd/MM HH:mm}", value, string.Join(", ", detail), frac);
        }).ToList();
    }

}
