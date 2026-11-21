using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Diagnostics;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Persistence;

namespace CoreBi.Desktop
{
    public sealed record DashComponent(string Icon, string Channel, string Name, string Model, string Target, Brush Meter,
                                       string Hero, string HeroUnit, double Fraction, IReadOnlyList<KeyValuePair<string, string>> Stats);

    public sealed record DashEvent(string Icon, string Title, string Detail, string Value, DateTime At, string Channel = "")
    {
        public string When => At.Date == DateTime.Today ? $"hoje {At:HH:mm}" : $"{At:dd/MM HH:mm}";
    }

    public sealed record DashRingItem(Brush Swatch, string Label, string Value, string Detail, string Target);
    public sealed record DashCheck(string Title, string Detail, bool Ok);

    /// <summary>Atalho da atividade recente vazia: um teste que ainda não rodou.</summary>
    public sealed class DashStart
    {
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Target { get; set; } = "";
        public string Tip { get; set; } = "";
    }
}

namespace CoreBi.Desktop.Views
{
    public partial class OverviewView : UserControl
    {
        internal const double EventRowHeight = 56;
        private const int MaxSamples = 900; // 15 min a 1 amostra/s
        private const int EventsVisibleMax = 5;
        private const int EventsHistoryKept = 50;

        private readonly record struct DashSample(DateTime At, float? CpuT, float? GpuT, float? CpuL, float? GpuL, float? CpuW, float? GpuW);

        private FeatureContext? _context;
        private readonly List<DashSample> _samples = new();
        private readonly List<DashEvent> _events = new();
        private DateTime _healthAt = DateTime.MinValue;
        private HardwareErrors? _errors;   // erros de hardware dos últimos 7 dias, lidos do registro de eventos fora da janela
        private DateTime _errorsAt = DateTime.MinValue;
        private bool _errorsBusy;
        private bool _metricChosen, _metricAuto;

        public OverviewView() => InitializeComponent();

        internal void Initialize(FeatureContext context)
        {
            _context = context;
            UpdateDashboard();
        }

        internal void Tick()
        {
            if (_context == null) return;
            _samples.Add(new DashSample(DateTime.Now, _context.Sensors.CpuTemperature, _context.PrimaryGpuTemp(),
                _context.Sensors.CpuLoad, _context.PrimaryGpuLoad(), _context.Sensors.CpuPower, _context.PrimaryGpuPower()));
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
            if (IsVisible) UpdateDashboard(full: false);
        }

        internal void AddEvent(string icon, string title, string detail, string value, string channel = "")
        {
            _events.Insert(0, new DashEvent(icon, title, detail, value, DateTime.Now, channel));
            if (_events.Count > 30) _events.RemoveAt(_events.Count - 1);
            if (IsVisible) ShowEvents();
        }

        internal void UpdateDashboard(bool full = true)
        {
            if (_context == null) return;
            if (_hoverX == null)
            {
                PickMetric();
                DrawChart();
            }
            UpdateComponents();
            UpdateRings();
            if (full)
            {
                UpdateScores();
                ShowEvents();
            }
            if (full || (DateTime.Now - _healthAt).TotalSeconds > 60) UpdateHealth();
        }

        internal void RedrawTheme() => UpdateDashboard();

        // Formatos gravados em BenchRecord.Summary (settings.json), lidos por posição:
        //   rt:   "1.499 pts · 150 FPS · Alta 1080p [· instável]"
        //   gpu:  "4.286 pts · 429 FPS · 1080p"
        //   sim:  "120 FPS · 1% low 95"
        //   mem:  "84 ns · 36 GB/s · mapa 0,19 s"
        //   disk: "C: · 2.929/3.320 MiB/s · 4K 14,6k IOPS"
        internal static DashEvent BenchEvent(BenchRecord r)
        {
            var parts = r.Summary.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string Part(int i) => i < parts.Length ? parts[i] : "";
            static string Join(IEnumerable<string> p) => string.Join(" · ", p.Where(s => s.Length > 0));
            if (r.Test.StartsWith("rt:", StringComparison.Ordinal))
                return new DashEvent("gpu", "Ray tracing", Join(new[] { Part(2), Part(1), Part(3) }), Part(0), r.Date, "Gpu");

            switch (r.Test)
            {
                case "gpu":
                    return new DashEvent("gpu", "Cena de jogo (GPU)", Join(parts.Skip(1)), Part(0), r.Date, "Gpu");
                case "sim":
                    return new DashEvent("\ue7fc", "Simulação de jogo (CPU)", Part(1), Part(0), r.Date, "Cpu");
                case "mem":
                    return new DashEvent("ram", "Benchmark de memória",
                        Join(new[] { Part(1).Length > 0 ? "Leitura " + Part(1) : "", Part(2).Replace("mapa ", "mapa em ") }), Part(0), r.Date, "Memory");
                case "disk":
                    {
                        string speeds = Part(1);
                        int slash = speeds.IndexOf('/');
                        int space = speeds.IndexOf(' ');
                        if (slash > 0 && space > slash)
                            return new DashEvent("storage", $"Benchmark de disco ({Part(0).TrimEnd(':')}:)",
                                Join(new[] { $"Escrita {speeds[(slash + 1)..space]} {speeds[(space + 1)..]}", Part(2) }),
                                $"{speeds[..slash]} {speeds[(space + 1)..]}", r.Date, "Storage");
                        return new DashEvent("storage", "Benchmark de disco", Join(parts.Skip(1)), Part(0), r.Date, "Storage");
                    }
                default:
                    return new DashEvent("\ue8b9", r.Test, Join(parts.Skip(1)), Part(0), r.Date);
            }
        }

        private IEnumerable<DashEvent> SavedEvents()
        {
            if (_context == null) yield break;
            foreach (var r in _context.Settings.BenchRuns)
            {
                string mode = r.Multi != null ? "Multi-core" : "Single-core";
                string cpu = string.IsNullOrWhiteSpace(r.Cpu) ? "" : $" · {r.Cpu}";
                if (r.Multi is double m) yield return new DashEvent("cpu", "Render de CPU", mode + cpu, $"{m:N0} pts", r.Date, "Cpu");
                else if (r.Single is double s) yield return new DashEvent("cpu", "Render de CPU", mode + cpu, $"{s:N0} pts", r.Date, "Cpu");
            }
            foreach (var r in _context.Settings.BenchRecords)
                yield return BenchEvent(r);
        }

        private void ShowEvents()
        {
            double room = ((FrameworkElement)DashEvents.Parent).ActualHeight;
            int slots = room > 0 ? Math.Clamp((int)(room / EventRowHeight), 1, EventsVisibleMax) : 3;
            var all = _events.Concat(SavedEvents()).OrderByDescending(e => e.At).ToList();
            var list = all.Take(slots).ToList();
            if (DashEvents.ItemsSource is IList<DashEvent> shown && shown.SequenceEqual(list)) return;
            DashEvents.ItemsSource = list;
            DashEventsAll.ItemsSource = all.Take(EventsHistoryKept).ToList();
            bool more = all.Count > list.Count;
            DashEventsMore.Visibility = more ? Visibility.Visible : Visibility.Collapsed;
            if (!more) ShowHistory(false);
            DashEventsEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string FormatReading(float? value, string fmt, string unit) => value is float x ? x.ToString(fmt) + unit : "—";

        private void UpdateComponents()
        {
            if (_context == null) return;
            static KeyValuePair<string, string> Stat(string label, string value) => new(label, value);

            var cpuLive = _context.ReadCpuLive();
            var cpuLoad = _context.Sensors.CpuLoad;
            var rows = new List<DashComponent>
            {
                new("cpu", "Cpu", "Processador", HardwareNaming.ShortCpuName(_context.CpuName), "PageSystem:Processador", ThemeManager.Brush("Cpu"),
                    FormatReading(cpuLoad, "0", " %"), "em uso", (cpuLoad ?? 0) / 100.0,
                    new[]
                    {
                        Stat("Temp.", FormatReading(_context.Sensors.CpuTemperature, "0", " °C")),
                        Stat("Clock", FormatReading(cpuLive.AvgClockMHz, "0", " MHz")),
                        Stat("Consumo", FormatReading(_context.Sensors.CpuPower, "0", " W")),
                    }),
            };

            if (_context.PrimaryGpu is { } gpu)
            {
                var live = GpuMonitor.ReadLive(gpu, _context.Sensors.Snapshot());
                rows.Add(new("gpu", "Gpu", "Placa de vídeo", HardwareNaming.ShortGpuName(gpu.Name), "PageGpu", ThemeManager.Brush("Gpu"),
                    FormatReading(live.LoadPct, "0", " %"), "em uso", (live.LoadPct ?? 0) / 100.0,
                    new[]
                    {
                        Stat("Temp.", FormatReading(live.TempC, "0", " °C")),
                        Stat("Clock", live.CoreClockMHz is float c ? $"{c:0} MHz" : "—"),
                        Stat("Consumo", FormatReading(live.PowerW, "0", " W")),
                    }));
            }
            DashComponents.ItemsSource = rows;
        }

        private void UpdateScores()
        {
            if (_context == null) return;
            var runs = _context.Settings.BenchRuns.Where(r => r.Multi != null).ToList();
            if (runs.Count > 0)
            {
                DashScore.Text = "";
                DashScore.Inlines.Add(new Run($"{runs[0].Multi:N0}"));
                var pts = new Run("\u2002pts") { FontSize = 12.5, FontStretch = FontStretches.Normal, FontWeight = FontWeights.Normal };
                pts.SetResourceReference(TextElement.FontFamilyProperty, "LabelFont");
                pts.SetResourceReference(TextElement.ForegroundProperty, "Muted");
                DashScore.Inlines.Add(pts);
                DashScoreSub.Text = $"Medido em {runs[0].Date:dd/MM 'às' HH:mm}";
                if (runs.Count > 1)
                {
                    double d = runs[0].Multi!.Value - runs[1].Multi!.Value;
                    DashScoreDelta.Text = d >= 0 ? $"+{d:N0} desde o anterior" : $"−{-d:N0} desde o anterior";
                    DashScoreDelta.SetResourceReference(TextBlock.ForegroundProperty, d > 0 ? "Ok" : "Muted");
                }
                else DashScoreDelta.Text = "";
            }
            else
            {
                DashScore.Text = "—";
                DashScoreDelta.Text = "";
                DashScoreSub.Text = "Nenhum teste ainda";
            }

            string Last(string test) => _context.Settings.BenchRecords.FirstOrDefault(r => r.Test == test)?.Summary.Split('·')[0].Trim() ?? "—";
            var single = _context.Settings.BenchRuns.FirstOrDefault(r => r.Single != null)?.Single;
            DashScores.ItemsSource = new[]
            {
                new KeyValuePair<string, string>("Render single-core", single is double s ? $"{s:N0} pts" : "—"),
                new KeyValuePair<string, string>("Cena de jogo (GPU)", Last("gpu")),
                new KeyValuePair<string, string>("Simulação de jogo", Last("sim")),
            };
            if (_context.CpuName.Length > 0 && _context.PrimaryGpu is { } gpu)
                DashMachine.Text = $"Leituras ao vivo de {Environment.MachineName}: {HardwareNaming.ShortCpuName(_context.CpuName)} e {HardwareNaming.ShortGpuName(gpu.Name)}.";
        }

        private void UpdateHealth()
        {
            if (_context == null) return;
            _healthAt = DateTime.Now;
            static DashCheck Item(bool ok, string title, string okText, string badText) => new(title, ok ? okText : badText, ok);

            // O registro de eventos do Windows leva ~100 ms para responder: é lido fora da janela, no máximo a cada minuto
            if ((_healthAt - _errorsAt).TotalSeconds > 60) ReadErrorsAsync();
            Skeleton.SetActive(DashHealthCard, _errors == null);
            if (_errors is not { } errs) return;

            var list = new List<DashCheck>();
            if (_context.Sensors.IsOpen) // enquanto detecta o hardware, ainda não dá para saber
            {
                var sensors = _context.Sensors.DiagnoseCpuSensors();
                list.Add(Item(sensors == SensorMonitor.CpuSensorProblem.None, "Sensores da CPU",
                    "Temperatura e consumo disponíveis.", SensorMonitor.Explain(sensors).Split('.')[0] + "."));
            }

            static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
            list.Add(Item(errs.Error == null && errs.Total == 0, "Erros de hardware",
                "Sem WHEA nem reinício da GPU em 7 dias.",
                errs.Error != null ? "Não foi possível ler o registro do Windows."
                    : $"{Count(errs.Whea, "erro WHEA", "erros WHEA")} e {Count(errs.GpuResets, "reinício", "reinícios")} da GPU em 7 dias."));

            DashHealth.ItemsSource = list;

            int alerts = list.Count(c => !c.Ok);
            DashHealthBadge.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DashHealthBadgeText.Text = alerts == 0 ? "Tudo certo" : alerts == 1 ? "1 alerta" : $"{alerts} alertas";
            Ui.SetTone(DashHealthBadge, alerts == 0 ? "Ok" : "Warn");
        }

        private async void ReadErrorsAsync()
        {
            if (_errorsBusy) return;
            _errorsBusy = true;
            try { _errors = await Task.Run(() => HardwareEventReader.ErrorsSince(DateTime.Now.AddDays(-7))); }
            finally { _errorsBusy = false; }
            _errorsAt = DateTime.Now;
            if (_context is { IsClosing: false }) UpdateHealth();
        }

        private void DashBench_Click(object sender, RoutedEventArgs e) => _context?.Navigate("PageBench");
        private void DashSensors_Click(object sender, RoutedEventArgs e) => _context?.Navigate("PageSensors");

        private void DashStart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string target }) _context?.Navigate(target);
        }

        private void DashGo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string target }) _context?.Navigate(target);
        }

        private void DashEventsMore_Click(object sender, RoutedEventArgs e) => ShowHistory(DashEventsAll.Visibility != Visibility.Visible);

        internal void ShowHistory(bool on)
        {
            DashEventsAll.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            DashEvents.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            DashEventsMore.Content = on ? "Mostrar menos" : "Ver histórico";
            if (on)
            {
                DashEventsAll.ApplyTemplate();
                (DashEventsAll.Template.FindName("HistoryScroll", DashEventsAll) as ScrollViewer)?.ScrollToTop();
            }
        }

        private void DashEvents_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // O host não pede altura, então o histórico precisa de uma altura própria (em linhas inteiras) para saber que não cabe e rolar.
            DashEventsAll.Height = Math.Max(EventRowHeight, Math.Floor(e.NewSize.Height / EventRowHeight) * EventRowHeight);
            if (e.HeightChanged) ShowEvents();
        }

        private void DashComponentsRow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged) return;
            bool wide = e.NewSize.Width >= 1000;
            Grid.SetRow(DashMemoryCard, wide ? 0 : 1);
            Grid.SetColumn(DashMemoryCard, wide ? 1 : 0);
            Grid.SetColumnSpan(DashMemoryCard, wide ? 1 : 2);
            DashMemoryCard.Margin = new Thickness(0, wide ? 0 : 16, 0, 0);
            Grid.SetColumnSpan(DashComponents, wide ? 1 : 2);
            DashComponents.Margin = new Thickness(0, 0, wide ? 0 : -16, 0);
        }
    }
}
