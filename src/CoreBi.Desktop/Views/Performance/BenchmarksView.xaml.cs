using System.IO;
using System.Windows;
using System.Windows.Controls;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Persistence;

namespace CoreBi.Desktop
{
    public sealed record BenchStat(string Label, string Value, string Unit = "", string Sub = "", string Tone = "", string? Tip = null);
    public sealed record MemRow(string Label, string Value, string Unit, string Sub = "", string? Hint = null);
    public sealed record BenchHistoryRow(string Date, string Value, string Detail, double Fraction);
}

namespace CoreBi.Desktop.Views
{
    public partial class BenchmarksView : UserControl, IDisposable
    {
        private FeatureContext? _context;
        private bool _gameBenchRunning;
        private CancellationTokenSource? _gameBenchCts;

        public BenchmarksView()
        {
            InitializeComponent();
            // Primeira visita: os nomes das peças (placas e unidades) chegam com a leitura do hardware
            Skeleton.SetActive(BenchConfigCard, true);
            IsVisibleChanged += (_, e) => { if (e.NewValue is true) CheckRtSupportOnce(); };
        }

        internal bool IsRunning => _benchCts != null || _gameBenchRunning;

        internal void Initialize(FeatureContext context)
        {
            _context = context;
            InitCpuBench();
            InitSimStage();
            ResetStages();
            ShowLastResults();
            UpdateCpuHistory();
            UpdateGameBenchHistory();
            Tick();
        }

        internal void SelectTest(string testTag)
        {
            if (BenchTestPanel.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.Tag == testTag) is { } tile)
                tile.IsChecked = true;
        }

        internal void RefreshHardware()
        {
            Skeleton.SetActive(BenchConfigCard, false);
            UpdatePlates();
            InitDiskBench();
        }

        internal void Tick()
        {
            if (_context == null) return;
            var cells = new List<BenchStat>();
            if (_context.Sensors.CpuTemperature is float t) cells.Add(new("Temperatura", $"{t:0.#}", "°C"));
            if (_context.ReadCpuLive().AvgClockMHz is float clock) cells.Add(new("Clock médio", $"{clock:0}", "MHz", Tip: "Média dos núcleos"));
            if (_context.Sensors.CpuPower is float p) cells.Add(new("Consumo", $"{p:0.#}", "W"));
            if (_context.Sensors.CpuLoad is float l) cells.Add(new("Carga", $"{l:0}", "%"));
            Ui.SetItems(BenchLive, cells);
            UpdatePlates();
        }

        private void BenchTest_Checked(object sender, RoutedEventArgs e)
        {
            if (BenchPanelRender == null) return;
            string test = (string)((FrameworkElement)sender).Tag;
            BenchPanelRender.Visibility = test == "render" ? Visibility.Visible : Visibility.Collapsed;
            BenchPanelGpu.Visibility = test == "gpu" ? Visibility.Visible : Visibility.Collapsed;
            BenchPanelRt.Visibility = test == "rt" ? Visibility.Visible : Visibility.Collapsed;
            BenchPanelSim.Visibility = test == "sim" ? Visibility.Visible : Visibility.Collapsed;
            BenchPanelMem.Visibility = test == "mem" ? Visibility.Visible : Visibility.Collapsed;
            BenchPanelDisk.Visibility = test == "disk" ? Visibility.Visible : Visibility.Collapsed;
            if (test == "disk") InitDiskBench();
            UpdatePlates();
            UpdateGameBenchHistory();
        }

        private void ResetStages()
        {
            GbResult.ItemsSource = GpuStats(null);
            RtResult.ItemsSource = RtStats(null);
            SimResult.ItemsSource = SimStats(null);
            MemResult.ItemsSource = MemRows(null, null, null);
            DiskResult.ItemsSource = DiskRows(null);
        }

        private void UpdatePlates()
        {
            if (_context == null) return;
            string cpu = HardwareNaming.ShortCpuName(_context.CpuName);
            if (cpu.Length == 0) cpu = _context.CpuName.Length > 0 ? _context.CpuName : "Processador";
            BenchDevice.Text = SimDevice.Text = cpu;
            BenchDeviceSpec.Text = SimDeviceSpec.Text = $"{Environment.ProcessorCount} threads";
            GbDevice.Text = RtDevice.Text = _context.PrimaryGpu?.Name ?? "Placa de vídeo";

            string temp = Path.GetPathRoot(Path.GetTempPath())?.TrimEnd('\\') ?? "";
            MemDeviceSpec.Text = MemoryInfo.ReadPhysicalUsage() is { } ram
                ? $"{ram.Total / 1073741824.0:0} GB instalados, carregamento pela unidade {temp}"
                : $"Carregamento pela unidade {temp}";
            UpdateDiskPlate();
        }

        /// <summary>
        /// A página abre com o último resultado de cada teste: os números guardados (Figures), a última passada do FPS (Trace) e,
        /// no estado, quando foi. Resultados de versões antigas, sem os números, mostram só a nota.
        /// </summary>
        private void ShowLastResults()
        {
            if (_context == null) return;
            if (_context.Settings.BenchRuns.FirstOrDefault() is { } run)
            {
                BenchMt.Text = run.Multi is double m ? $"{m:N0}" : "—";
                BenchSt.Text = run.Single is double s ? $"{s:N0}" : "—";
                BenchRatio.Text = run.Multi is double mm && run.Single is double ss && ss > 0 ? $"{mm / ss:0.0}" : "—";
                BenchMtVs.Text = run.Sustained ? "mínimo de 10 min" : "";
                BenchElapsed.Text = LastNote(run.Date);
            }

            var sim = Last(r => r.Test == "sim");
            SimResult.ItemsSource = Restore(sim, SimStats(null), v => $"{v:0}");
            SimDetail.Text = sim != null ? LastNote(sim.Date) : "";

            var gpu = Last(r => r.Test == "gpu");
            GbResult.ItemsSource = Restore(gpu, GpuStats(null), v => $"{v:N0}");
            GbDetail.Text = gpu != null ? LastNote(gpu.Date) : "";
            if (gpu != null) GbTrace.Load(gpu.Trace, "Última passada · " + ReadoutFormat.When(gpu.Date));

            var rt = Last(r => r.Test.StartsWith("rt:", StringComparison.Ordinal));
            RtResult.ItemsSource = Restore(rt, RtStats(null), v => $"{v:N0}");
            RtDetail.Text = rt != null ? LastNote(rt.Date) : "";
            if (rt != null) RtTrace.Load(rt.Trace, "Última passada · " + ReadoutFormat.When(rt.Date));

            var mem = Last(r => r.Test == "mem");
            MemResult.ItemsSource = Merge(MemRows(null, null, null), mem?.Figures);
            MemDetail.Text = mem?.Figures != null ? LastNote(mem.Date) : "";
        }

        private static string LastNote(DateTime at) => "Último teste " + ReadoutFormat.When(at);

        private BenchRecord? Last(Func<BenchRecord, bool> test) =>
            _context?.Settings.BenchRecords.Where(test).OrderByDescending(r => r.Date).FirstOrDefault();

        private static BenchFigure Figure(BenchStat s) => new(s.Label, s.Value, s.Unit, s.Sub, s.Tone, s.Tip);

        private static BenchFigure Figure(MemRow r) => new(r.Label, r.Value, r.Unit, r.Sub, "", r.Hint);

        /// <summary>Os números guardados de um teste, ou o modelo vazio com só a nota na primeira célula (resultados antigos).</summary>
        private static List<BenchStat> Restore(BenchRecord? r, List<BenchStat> empty, Func<double, string> score)
        {
            if (r == null) return empty;
            if (r.Figures is { Count: > 0 } figures) return figures.Select(f => new BenchStat(f.Label, f.Value, f.Unit, f.Sub, f.Tone, f.Tip)).ToList();
            var list = empty.ToList();
            list[0] = list[0] with { Value = score(r.Score) };
            return list;
        }

        /// <summary>As linhas de memória ou disco com os valores guardados (os nomes e as explicações vêm sempre da versão atual).</summary>
        private static List<MemRow> Merge(List<MemRow> rows, List<BenchFigure>? figures) =>
            figures is not { Count: > 0 } ? rows
                : rows.Select(row => figures.FirstOrDefault(f => f.Label == row.Label) is { } f ? row with { Value = f.Value, Sub = f.Sub } : row).ToList();

        /// <summary>Partes do resumo salvo ("valor · detalhe · detalhe"), o mesmo formato que a Visão geral lê (OverviewView.BenchEvent).</summary>
        private static string[] SummaryParts(string summary) =>
            summary.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        private static string VsBest(double score, IEnumerable<double> previous)
        {
            var prev = previous.Where(v => v > 0 && double.IsFinite(v)).ToList();
            if (prev.Count == 0 || score <= 0) return "";
            double pct = (score / prev.Max() - 1) * 100;
            if (Math.Abs(pct) < 0.5) return "igual ao melhor anterior";
            return pct > 0 ? $"{pct:0} % acima do melhor anterior" : $"{-pct:0} % abaixo do melhor anterior";
        }

        private void SaveBenchRecord(string test, double score, string summary, IEnumerable<BenchFigure>? figures = null, List<double>? trace = null)
        {
            if (_context == null) return;
            var record = new BenchRecord(DateTime.Now, test, score, summary) { Figures = figures?.ToList(), Trace = trace };
            var kind = OverviewView.BenchEvent(record);
            _context.RecordEvent(kind.Icon, "Benchmark concluído", summary, "", kind.Channel);
            _context.Settings.BenchRecords.Insert(0, record);
            _context.Settings.BenchRecords = _context.Settings.BenchRecords
                .GroupBy(r => r.Test)
                .SelectMany(g => g.Take(10))
                .OrderByDescending(r => r.Date)
                .ToList();
            _context.SaveSettings();
            UpdateGameBenchHistory();
        }

        private void UpdateGameBenchHistory()
        {
            if (_context == null) return;
            IEnumerable<BenchRecord> Of(Func<BenchRecord, bool> test) => _context.Settings.BenchRecords.Where(test);
            // A barra só compara o que é comparável: a cena de jogo na mesma resolução, o ray tracing no mesmo preset e
            // resolução, o disco na mesma unidade.
            GbHistory.ItemsSource = History(Of(r => r.Test == "gpu"), r => SummaryParts(r.Summary).LastOrDefault() ?? "");
            RtHistory.ItemsSource = History(Of(r => r.Test.StartsWith("rt:", StringComparison.Ordinal)), r => r.Test);
            SimHistory.ItemsSource = History(Of(r => r.Test == "sim"), _ => "");
            MemHistory.ItemsSource = History(Of(r => r.Test == "mem"), _ => "");
            DiskHistory.ItemsSource = History(Of(r => r.Test == "disk"), r => SummaryParts(r.Summary).FirstOrDefault() ?? "", valueIndex: 1);
        }

        private static List<BenchHistoryRow> History(IEnumerable<BenchRecord> records, Func<BenchRecord, string> group, int valueIndex = 0)
        {
            var list = records.OrderByDescending(r => r.Date).ToList();
            var best = list.GroupBy(group).ToDictionary(g => g.Key, g => g.Max(r => r.Score));
            return list.Select(r =>
            {
                var parts = SummaryParts(r.Summary);
                string value = valueIndex < parts.Length ? parts[valueIndex] : r.Summary;
                string detail = string.Join(", ", parts.Where((_, i) => i != valueIndex));
                double b = best[group(r)];
                return new BenchHistoryRow(ReadoutFormat.When(r.Date, compact: true), value, detail, b > 0 ? r.Score / b : 0);
            }).ToList();
        }

        private void SetGameBenchRunning(Button button, bool running)
        {
            _gameBenchRunning = running;
            Ui.SetRunning(button, running, "Iniciar teste", "Cancelar");
            BenchTestPanel.IsEnabled = !running;
        }

        private void View_SizeChanged(object sender, SizeChangedEventArgs e) =>
            BenchTestPanel.Rows = e.NewSize.Width < 1080 ? 2 : 1;

        public void Dispose()
        {
            _benchTimer?.Stop();
            try { _benchCts?.Cancel(); } catch (ObjectDisposedException) { }
            try { _gameBenchCts?.Cancel(); } catch (ObjectDisposedException) { }
            _gpuBench?.Cancel();
            _rtBench?.Cancel();
        }
    }
}
