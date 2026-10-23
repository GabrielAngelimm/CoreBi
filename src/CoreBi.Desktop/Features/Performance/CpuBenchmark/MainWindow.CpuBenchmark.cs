using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
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
        UpdateBenchHistory();
        UpdateBenchLive();
    }

    internal bool BenchRunning => _benchCts != null;

    internal async void BtnBench_Click(object sender, RoutedEventArgs e)
    {
        if (BenchRunning) { _benchCts!.Cancel(); return; }
        if (TestRunning || _gameBenchRunning) { BenchHint.Text = "Pare o outro teste antes de rodar o benchmark."; return; }
        using var workload = _workloads.TryBegin("CPU render");
        if (workload == null) { BenchHint.Text = "Termine o outro teste antes."; return; }

        string mode = CheckedTag(BenchModePanel) ?? "both";
        bool sustained = CheckedTag(BenchDurationPanel) == "10";
        var minDuration = sustained ? TimeSpan.FromMinutes(10) : TimeSpan.Zero;
        int threads = Environment.ProcessorCount;

        _benchCts = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
        var ct = _benchCts.Token;
        BtnBench.Content = "PARAR";
        BtnBench.Style = (Style)FindResource("OutlineButton");
        BenchModePanel.IsEnabled = BenchDurationPanel.IsEnabled = BenchTestPanel.IsEnabled = false;
        BenchHint.Text = "";
        BenchIdle.Visibility = Visibility.Collapsed;
        BenchMt.Text = BenchSt.Text = "—";
        BenchRatio.Text = "";
        _benchStart = DateTime.Now;
        _benchTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => UpdateBenchView(), Dispatcher);
        _benchTimer.Start();

        double? mt = null, st = null;
        try
        {
            if (mode is "mt" or "both")
            {
                _benchPhase = $"Multi-core · {threads} threads";
                _benchPhaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(threads, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { mt = r.Score; BenchMt.Text = $"{r.Score:N0}"; }
            }
            if (!ct.IsCancellationRequested && mode is "st" or "both")
            {
                _benchPhase = "Single-core · 1 thread";
                _benchPhaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(1, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { st = r.Score; BenchSt.Text = $"{r.Score:N0}"; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BenchHint.Text = "Falha no teste: " + ex.Message; _benchCts?.Cancel(); }

        _benchTimer.Stop();
        UpdateBenchView();
        bool cancelled = ct.IsCancellationRequested;
        _benchCts = null;
        BtnBench.Content = "INICIAR";
        BtnBench.Style = (Style)FindResource("PrimaryButton");
        BenchModePanel.IsEnabled = BenchDurationPanel.IsEnabled = BenchTestPanel.IsEnabled = true;
        BenchBuckets.Children.Clear();

        var total = DateTime.Now - _benchStart;
        if (cancelled)
        {
            BenchStatus.Text = "Cancelado";
            return;
        }
        BenchStatus.Text = $"Concluído em {total:m\\:ss}";
        if (mt is double m && st is double s)
            BenchRatio.Text = $"Multi ÷ single: {m / s:0.0}x com {threads} threads " +
                              $"({Environment.ProcessorCount / 2} núcleos com SMT costumam render 8–12x).";

        _settings.BenchRuns.Insert(0, new BenchRun(DateTime.Now, mt, st, sustained, GameCatalog.ShortCpuName(_cpuName)));
        AddEvent("", "Benchmark", "Render de CPU", mt is double mte ? $"{mte:N0} pts" : st is double ste ? $"{ste:N0} pts" : "");
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
            var rect = new Rectangle { Width = w, Height = h, Stroke = Brushes.White, StrokeThickness = 1.5 };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            BenchBuckets.Children.Add(rect);
        }

        if (BenchRunning)
        {
            double frac = _bench.BucketsTotal > 0 ? (double)_bench.BucketsDone / _bench.BucketsTotal : 0;
            BenchProgress.ScaleX = frac;
            // Tempo restante da passada atual, pela velocidade até agora (só depois de 3% para não oscilar).
            var phaseElapsed = DateTime.Now - _benchPhaseStart;
            string eta = frac >= 0.03 ? $" · faltam ~{TimeSpan.FromSeconds(phaseElapsed.TotalSeconds * (1 - frac) / frac):m\\:ss}" : "";
            BenchStatus.Text = $"{_benchPhase} · {frac:P0}{eta}";
            BenchElapsed.Text = (DateTime.Now - _benchStart).ToString(@"mm\:ss");
        }
        else BenchProgress.ScaleX = 0;
    }

    /// <summary>Temperatura, clock médio dos núcleos e consumo da CPU (atualizado junto com os sensores).</summary>
    internal void UpdateBenchLive()
    {
        var clocks = _monitor.Rows.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock &&
                                              r.Name.StartsWith("Core #") && r.RawValue > 0).Select(r => r.RawValue!.Value).ToList();
        BenchLive.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("CPU", GameCatalog.ShortCpuName(_cpuName)),
            new KeyValuePair<string, string>("Threads", $"{Environment.ProcessorCount}"),
            new KeyValuePair<string, string>("Temperatura", _monitor.CpuTemperature is float t ? $"{t:0.#} °C" : "—"),
            new KeyValuePair<string, string>("Clock médio", clocks.Count > 0 ? $"{clocks.Average():0} MHz" : "—"),
            new KeyValuePair<string, string>("Consumo", _monitor.CpuPower is float p ? $"{p:0.#} W" : "—"),
            new KeyValuePair<string, string>("Carga", _monitor.CpuLoad is float l ? $"{l:0} %" : "—"),
        };
    }

    internal void UpdateBenchHistory()
    {
        var runs = _settings.BenchRuns;
        BenchHistoryEmpty.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        double bestMt = runs.Max(r => r.Multi) ?? 1, bestSt = runs.Max(r => r.Single) ?? 1;
        BenchHistory.ItemsSource = runs.Select(r =>
        {
            string label = $"{r.Date:dd/MM HH:mm}{(r.Sustained ? " · 10 min" : "")}";
            string value = $"{(r.Multi is double m ? $"{m:N0}" : "—")} / {(r.Single is double s ? $"{s:N0}" : "—")}";
            double frac = r.Multi is double mm ? mm / bestMt : r.Single is double ss ? ss / bestSt : 0;
            return new MeterItem(label, value, frac);
        }).ToList();
    }

}
