using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Performance.Benchmarks;
using CoreBi.Engine.Persistence;

namespace CoreBi.Desktop.Views;

public partial class BenchmarksView
{
    private readonly BenchRenderer _bench = new();
    private WriteableBitmap? _benchBitmap;
    private CancellationTokenSource? _benchCts;
    private DispatcherTimer? _benchTimer;
    private DateTime _benchStart;
    private string _benchPhase = "";
    private DateTime _benchPhaseStart;

    private void InitCpuBench()
    {
        _benchBitmap = new WriteableBitmap(BenchRenderer.Width, BenchRenderer.Height, 96, 96, PixelFormats.Bgra32, null);
        BenchImage.Source = _benchBitmap;
    }

    private async void BtnBench_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null) return;
        if (_benchCts != null) { _benchCts.Cancel(); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.CpuRender);
        if (workload == null) { BenchHint.Text = "Termine o outro teste antes."; return; }

        string mode = Ui.CheckedTag(BenchModePanel) ?? "both";
        bool sustained = Ui.CheckedTag(BenchDurationPanel) == "10";
        var minDuration = sustained ? TimeSpan.FromMinutes(10) : TimeSpan.Zero;
        int threads = Environment.ProcessorCount;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        _benchCts = cts;
        var ct = cts.Token;
        Ui.SetRunning(BtnBench, true, "Iniciar teste", "Cancelar");
        BenchOptions.IsEnabled = BenchDurationSection.IsEnabled = BenchTestPanel.IsEnabled = false;
        BenchHint.Text = "";
        BenchIdle.Visibility = BenchPreview.Visibility = Visibility.Collapsed;
        BenchMt.Text = BenchSt.Text = BenchRatio.Text = "—";
        BenchMtVs.Text = BenchStVs.Text = BenchRatioSub.Text = "";
        _benchStart = DateTime.Now;
        _benchTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => UpdateCpuView(), Dispatcher);
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
        catch (Exception ex) { BenchHint.Text = "Falhou: " + ex.Message; cts.Cancel(); }
        finally
        {
            _benchTimer.Stop();
            UpdateCpuView();
            _benchCts = null;
            // Parado no meio ou concluído, a barra desliza de volta ao zero (a imagem e os números ficam)
            Ui.Rewind(BenchProgress);
            Ui.SetRunning(BtnBench, false, "Iniciar teste", "Cancelar");
            BenchOptions.IsEnabled = BenchDurationSection.IsEnabled = BenchTestPanel.IsEnabled = true;
            BenchBuckets.Children.Clear();
            BenchElapsed.Text = "";
        }

        var total = DateTime.Now - _benchStart;
        if (ct.IsCancellationRequested)
        {
            // Cancelado por você, nada a dizer: a placa volta ao estado de espera (como no Teste de carga)
            BenchStatus.Text = "";
            return;
        }
        BenchStatus.Text = $"Concluído em {total:m\\:ss}";
        if (mt is double m && st is double s)
        {
            BenchRatio.Text = $"{m / s:0.0}";
            BenchRatioSub.Text = $"com {threads} threads";
        }
        // Só compara com passadas da mesma duração: a de 10 min aquece o processador e rende menos.
        var previous = _context.Settings.BenchRuns.Where(r => r.Sustained == sustained).ToList();
        if (mt is double mv) BenchMtVs.Text = VsBest(mv, previous.Select(r => r.Multi ?? 0));
        if (st is double sv) BenchStVs.Text = VsBest(sv, previous.Select(r => r.Single ?? 0));

        _context.Settings.BenchRuns.Insert(0, new BenchRun(DateTime.Now, mt, st, sustained, HardwareNaming.ShortCpuName(_context.CpuName)));
        _context.RecordEvent("cpu", "Benchmark", "Render de CPU", mt is double mte ? $"{mte:N0} pts" : st is double ste ? $"{ste:N0} pts" : "", "Cpu");
        _context.Settings.BenchRuns = BenchRun.Normalize(_context.Settings.BenchRuns);
        _context.SaveSettings();
        UpdateCpuHistory();
    }

    private void UpdateCpuView()
    {
        if (_benchBitmap == null) return;
        _benchBitmap.WritePixels(new Int32Rect(0, 0, BenchRenderer.Width, BenchRenderer.Height),
            _bench.Pixels, BenchRenderer.Width * 4, 0);

        BenchBuckets.Children.Clear();
        foreach (int b in _bench.Active)
        {
            if (b < 0) continue;
            var (x, y, w, h) = _bench.Buckets[b];
            var rect = new Rectangle { Width = w, Height = h, Stroke = ThemeManager.Brush("Signal"), StrokeThickness = 2 };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            BenchBuckets.Children.Add(rect);
        }

        if (_benchCts != null)
        {
            double frac = _bench.BucketsTotal > 0 ? (double)_bench.BucketsDone / _bench.BucketsTotal : 0;
            BenchProgress.ScaleX = frac;
            var phaseElapsed = DateTime.Now - _benchPhaseStart;
            // Tempo restante da passada atual, pela velocidade até agora (só depois de 3 % para não oscilar).
            string eta = frac >= 0.03 ? $", faltam cerca de {TimeSpan.FromSeconds(phaseElapsed.TotalSeconds * (1 - frac) / frac):m\\:ss}" : "";
            BenchStatus.Text = $"{_benchPhase}, {frac * 100:0} %";
            BenchElapsed.Text = (DateTime.Now - _benchStart).ToString(@"mm\:ss") + eta;
        }
        else BenchProgress.ScaleX = 0;
    }

    private void UpdateCpuHistory()
    {
        if (_context == null) return;
        var runs = _context.Settings.BenchRuns;
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
            return new BenchHistoryRow(ReadoutFormat.When(r.Date, compact: true), value, string.Join(", ", detail), frac);
        }).ToList();
        BenchHistoryClear.Visibility = runs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BenchHistoryClear_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null || !ConfirmButton.Confirm(sender)) return;
        _context.Settings.BenchRuns.Clear();
        _context.SaveSettings();
        UpdateCpuHistory();
    }
}
