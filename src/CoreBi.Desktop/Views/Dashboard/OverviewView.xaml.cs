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
    /// <summary>
    /// Cartão de componente da Visão geral (processador, placa de vídeo). O mesmo objeto vive a sessão inteira e só os valores
    /// mudam a cada segundo: trocar a lista refazia os dois cartões a cada tique, e o esqueleto deles não teria como sair suave.
    /// </summary>
    public sealed class DashComponent : ObservableObject
    {
        private string _model = "", _hero = "—";
        private double _fraction;
        private bool _loading = true;

        public DashComponent(string icon, string channel, string name, string target, string linkTip)
        {
            Icon = icon;
            Channel = channel;
            Name = name;
            Target = target;
            LinkTip = linkTip;
        }

        public string Icon { get; }
        public string Channel { get; }
        public string Name { get; }
        public string Target { get; }
        public string LinkTip { get; }
        public string HeroUnit => "em uso";
        public Brush Meter => ThemeManager.Brush(Channel);
        public DashStat[] Stats { get; } = { new("Temp."), new("Clock"), new("Consumo") };
        public string Model { get => _model; set => Set(ref _model, value); }
        public string Hero { get => _hero; set => Set(ref _hero, value); }
        public double Fraction { get => _fraction; set => Set(ref _fraction, value); }
        /// <summary>Ainda sem nome ou sem a primeira leitura dos sensores: o cartão mostra o esqueleto.</summary>
        public bool Loading { get => _loading; set => Set(ref _loading, value); }

        internal void RedrawTheme() => OnPropertyChanged(nameof(Meter));
    }

    public sealed class DashStat : ObservableObject
    {
        private string _value = "—";
        public DashStat(string key) => Key = key;
        public string Key { get; }
        public string Value { get => _value; set => Set(ref _value, value); }
    }

    public sealed record DashEvent(string Icon, string Title, string Detail, string Value, DateTime At, string Channel = "")
    {
        public string When => ReadoutFormat.When(At, compact: true);
    }

    public sealed record DashRingItem(Brush Swatch, string Label, string Value, string Detail, string Target);
    public sealed record DashCheck(string Icon, string Title, string Detail, bool Ok);

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

        private readonly DashComponent _cpuCard = new("cpu", "Cpu", "Processador", "PageSystem:Processador", "Abrir o processador em Sistema");
        private readonly DashComponent _gpuCard = new("gpu", "Gpu", "Placa de vídeo", "PageGpu", "Abrir Placa de vídeo");
        private readonly DateTime _bornAt = DateTime.Now;

        public OverviewView()
        {
            InitializeComponent();
            // Na abertura, antes de o hardware ser lido: os dois cartões já no lugar (o da placa de vídeo não surge depois) e
            // esqueleto no gráfico e nos anéis, desde o primeiro quadro
            DashComponents.ItemsSource = new[] { _cpuCard, _gpuCard };
            Skeleton.Set(true, DashChartCard, DashMemoryCard);
        }

        internal void Initialize(FeatureContext context)
        {
            _context = context;
            UpdateDashboard();
        }

        /// <summary>
        /// Ainda lendo o hardware da abertura (nomes, placa de vídeo e a primeira leitura dos sensores). Com um teto de 10 s: um
        /// sensor que não responde não deixa a página em esqueleto para sempre.
        /// </summary>
        private bool Warming(bool ready) => !ready && (DateTime.Now - _bornAt).TotalSeconds < 10;

        internal void Tick()
        {
            if (_context == null) return;
            AddSample();
            if (IsVisible) UpdateDashboard(full: false);
        }

        private void AddSample()
        {
            if (_context == null) return;
            _samples.Add(new DashSample(DateTime.Now, _context.Sensors.CpuTemperature, _context.PrimaryGpuTemp(),
                _context.Sensors.CpuLoad, _context.PrimaryGpuLoad(), _context.Sensors.CpuPower, _context.PrimaryGpuPower()));
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        }

        /// <summary>A primeira leitura dos sensores chegou (ou falhou): a amostra entra já, sem esperar o próximo tique.</summary>
        internal void SensorsReady()
        {
            if (_context?.Sensors.IsOpen == true) AddSample();
            UpdateDashboard();
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
            Skeleton.SetActive(DashChartCard, Warming(_context.SensorsRead));
            // Uma leitura completa da placa por tique (seis consultas NVML), dividida entre o cartão e o anel da VRAM
            var gpuLive = _context.PrimaryGpu is { } gpu ? GpuMonitor.ReadLive(gpu, _context.Sensors.Snapshot()) : null;
            UpdateComponents(gpuLive);
            UpdateRings(gpuLive);
            if (full)
            {
                UpdateScores();
                ShowEvents();
            }
            if (full || (DateTime.Now - _healthAt).TotalSeconds > 60 || Skeleton.GetActive(DashHealthCard)) UpdateHealth();
        }

        internal void RedrawTheme()
        {
            _cpuCard.RedrawTheme();
            _gpuCard.RedrawTheme();
            UpdateDashboard();
        }

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
                return new DashEvent("gpu", ReadoutFormat.BenchTitle(r.Test), Join(new[] { Part(2), Part(1), Part(3) }), Part(0), r.Date, "Gpu");

            switch (r.Test)
            {
                case "gpu":
                    return new DashEvent("gpu", ReadoutFormat.BenchTitle(r.Test), Join(parts.Skip(1)), Part(0), r.Date, "Gpu");
                case "sim":
                    return new DashEvent("\ue7fc", ReadoutFormat.BenchTitle(r.Test), Part(1), Part(0), r.Date, "Cpu");
                case "mem":
                    return new DashEvent("ram", ReadoutFormat.BenchTitle(r.Test),
                        Join(new[] { Part(1).Length > 0 ? "Leitura " + Part(1) : "", Part(2).Replace("mapa ", "mapa em ") }), Part(0), r.Date, "Memory");
                case "disk":
                    {
                        string speeds = Part(1);
                        int slash = speeds.IndexOf('/');
                        int space = speeds.IndexOf(' ');
                        if (slash > 0 && space > slash)
                            return new DashEvent("storage", $"{ReadoutFormat.BenchTitle(r.Test)} ({Part(0).TrimEnd(':')}:)",
                                Join(new[] { $"Escrita {speeds[(slash + 1)..space]} {speeds[(space + 1)..]}", Part(2) }),
                                $"{speeds[..slash]} {speeds[(space + 1)..]}", r.Date, "Storage");
                        return new DashEvent("storage", ReadoutFormat.BenchTitle(r.Test), Join(parts.Skip(1)), Part(0), r.Date, "Storage");
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
                if (r.Multi is double m) yield return new DashEvent("cpu", ReadoutFormat.BenchTitle("render"), mode + cpu, $"{m:N0} pts", r.Date, "Cpu");
                else if (r.Single is double s) yield return new DashEvent("cpu", ReadoutFormat.BenchTitle("render"), mode + cpu, $"{s:N0} pts", r.Date, "Cpu");
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

        private void UpdateComponents(GpuLiveSample? live)
        {
            if (_context == null) return;
            bool sensors = _context.SensorsRead;

            var cpuLive = _context.ReadCpuLive();
            var cpuLoad = _context.Sensors.CpuLoad;
            _cpuCard.Model = HardwareNaming.ShortCpuName(_context.CpuName);
            _cpuCard.Hero = FormatReading(cpuLoad, "0", " %");
            _cpuCard.Fraction = (cpuLoad ?? 0) / 100.0;
            _cpuCard.Stats[0].Value = FormatReading(_context.Sensors.CpuTemperature, "0", " °C");
            _cpuCard.Stats[1].Value = FormatReading(cpuLive.AvgClockMHz, "0", " MHz");
            _cpuCard.Stats[2].Value = FormatReading(_context.Sensors.CpuPower, "0", " W");
            _cpuCard.Loading = Warming(_context.HardwareRead && sensors);

            if (_context.PrimaryGpu is { } gpu && live != null)
            {
                _gpuCard.Model = HardwareNaming.ShortGpuName(gpu.Name);
                _gpuCard.Hero = FormatReading(live.LoadPct, "0", " %");
                _gpuCard.Fraction = (live.LoadPct ?? 0) / 100.0;
                _gpuCard.Stats[0].Value = FormatReading(live.TempC, "0", " °C");
                _gpuCard.Stats[1].Value = live.CoreClockMHz is float c ? $"{c:0} MHz" : "—";
                _gpuCard.Stats[2].Value = FormatReading(live.PowerW, "0", " W");
            }
            _gpuCard.Loading = Warming(_context.HardwareRead && sensors);

            // Sem placa de vídeo (lida e não achada), o cartão dela sai; até a leitura terminar ele fica, em esqueleto
            bool gpuShown = _context.PrimaryGpu != null || !_context.HardwareRead;
            if (DashComponents.Items.Count != (gpuShown ? 2 : 1))
                DashComponents.ItemsSource = gpuShown ? new[] { _cpuCard, _gpuCard } : new[] { _cpuCard };
        }

        private void UpdateScores()
        {
            if (_context == null) return;
            var runs = _context.Settings.BenchRuns.Where(r => r.Multi != null).ToList();
            if (runs.Count > 0)
            {
                DashScore.Text = "";
                DashScore.Inlines.Add(new Run($"{runs[0].Multi:N0}"));
                var pts = new Run("\u2002pts") { FontSize = 13, FontStretch = FontStretches.Normal, FontWeight = FontWeights.Normal };
                pts.SetResourceReference(TextElement.FontFamilyProperty, "LabelFont");
                pts.SetResourceReference(TextElement.ForegroundProperty, "Muted");
                DashScore.Inlines.Add(pts);
                DashScoreSub.Text = $"Render de CPU multi-core · {runs[0].Date:dd/MM 'às' HH:mm}";
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
            // Só o nome do computador: as peças já estão nos cartões logo abaixo
            DashMachine.Text = $"Leituras ao vivo de {Environment.MachineName}.";
        }

        private void UpdateHealth()
        {
            if (_context == null) return;
            _healthAt = DateTime.Now;
            static DashCheck Item(string icon, bool ok, string title, string okText, string badText) => new(icon, title, ok ? okText : badText, ok);

            // O registro de eventos do Windows leva ~100 ms para responder: é lido fora da janela, no máximo a cada minuto
            if ((_healthAt - _errorsAt).TotalSeconds > 60) ReadErrorsAsync();
            // Os dois itens de uma vez: o dos sensores só existe depois da primeira leitura, e entrar depois empurrava a atividade recente
            Skeleton.SetActive(DashHealthCard, _errors == null || Warming(_context.SensorsRead));
            if (_errors is not { } errs || Warming(_context.SensorsRead)) return;

            var list = new List<DashCheck>();
            if (_context.Sensors.IsOpen) // enquanto detecta o hardware, ainda não dá para saber
            {
                var sensors = _context.Sensors.DiagnoseCpuSensors();
                list.Add(Item("temp", sensors == SensorMonitor.CpuSensorProblem.None, "Sensores da CPU",
                    "Temperatura e consumo disponíveis.", SensorMonitor.Explain(sensors).Split('.')[0] + "."));
            }

            static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
            list.Add(Item("board", errs.Error == null && errs.Total == 0, "Erros de hardware",
                "Sem WHEA nem reinício da GPU em 7 dias.",
                errs.Error != null ? "Não foi possível ler o registro do Windows."
                    : $"{Count(errs.Whea, "erro WHEA", "erros WHEA")} e {Count(errs.GpuResets, "reinício", "reinícios")} da GPU em 7 dias."));

            Ui.SetItems(DashHealth, list); // relida a cada minuto: só troca se mudou, para os itens não piscarem
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
