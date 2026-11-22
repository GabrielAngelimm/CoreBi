using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Desktop
{
    /// <summary>
    /// Medidor do Gargalo: leitura, fração 0–1 na cor do componente (Channel: Cpu, Gpu, Memory, Danger; vazio = aço) e a marca do
    /// limite que a classificação usa (Mark, 0–1; NaN = sem marca). Atualizado no lugar a cada leitura.
    /// </summary>
    public sealed class LimitReading : ObservableObject
    {
        private string _label, _value = "—", _sub = "", _tone = "";
        private double _fraction;

        public LimitReading(string label, string channel, double mark = double.NaN, string? tip = null, string? hint = null)
        {
            _label = label;
            Channel = channel;
            Mark = mark;
            Tip = tip;
            Hint = hint is null ? "" : "  " + hint;
        }

        public string Channel { get; }
        public double Mark { get; }
        public string? Tip { get; }
        public string Hint { get; }
        public bool HasMark => !double.IsNaN(Mark);
        public GridLength MarkLeft => new(HasMark ? Mark : 0, GridUnitType.Star);
        public GridLength MarkRight => new(HasMark ? 1 - Mark : 1, GridUnitType.Star);

        public string Label { get => _label; set => Set(ref _label, value); }
        public string Value { get => _value; set => Set(ref _value, value); }
        public string Sub { get => _sub; set => Set(ref _sub, value); }
        public string Tone { get => _tone; set => Set(ref _tone, value); }
        public double Fraction { get => _fraction; set => Set(ref _fraction, Math.Clamp(value, 0, 1)); }
    }

    public sealed record RulerStat(string Label, string Value, string Unit, bool First, string? Tip = null, string? HintText = null)
    {
        public string Hint => HintText is null ? "" : "  " + HintText;
    }
}

namespace CoreBi.Desktop.Views
{
    public partial class BottleneckView : UserControl
    {
        private const string SessionTip = "Precisa de um jogo aberto em primeiro plano. Mede pelo tempo escolhido e mostra por quanto tempo cada peça limitou o FPS.";

        private FeatureContext? _context;
        private BottleneckSession? _session;
        private double _balanceRatio = 1;

        private readonly LimitReading _gpu = new("Uso da GPU", "Gpu", LoadSample.GpuLimitPct / 100.0,
            "A partir de 90 % a placa de vídeo é o limite (o normal em jogos pesados).", "placa de vídeo");
        private readonly LimitReading _cpu = new("Núcleo mais ocupado da CPU", "Cpu", LoadSample.CpuLimitPct / 100.0,
            "A partir de 85 % num núcleo (ou no total) a CPU passa a limitar o FPS.", "processador");
        private readonly LimitReading _vram = new("VRAM em uso", "Gpu", LoadSample.VramLimitPct / 100.0,
            "A partir de 97 % a memória de vídeo está cheia.", "memória da placa de vídeo");
        private readonly LimitReading _ram = new("RAM em uso", "Memory", LoadSample.RamLimitPct / 100.0,
            "A partir de 92 % a memória RAM está cheia.", "memória do PC");
        private readonly LimitReading _gpuTemp = new("Temperatura da GPU", "Gpu", LoadSample.ThermalLimitC / 100.0,
            "A partir de 95 °C, ou quando o driver reduz o clock pela temperatura, o limite é térmico.");
        private readonly LimitReading _cpuTemp = new("Temperatura da CPU", "Cpu", LoadSample.ThermalLimitC / 100.0,
            "A partir de 95 °C o limite é térmico.");

        public BottleneckView() => InitializeComponent();

        internal bool HasActiveSession => _session is { Done: false };

        internal void Initialize(FeatureContext context)
        {
            _context = context;
            BnMeters.ItemsSource = new[] { _gpu, _cpu, _vram, _ram, _gpuTemp, _cpuTemp };
            Skeleton.SetActive(BnBalanceCard, true); // o equilíbrio do hardware espera os nomes da CPU e da placa de vídeo
        }

        internal void RefreshHardware()
        {
            if (_context == null) return;
            Skeleton.SetActive(BnBalanceCard, false);
            string gpuName = _context.PrimaryGpu?.Name ?? "";
            double? gpuPerf = HardwareCatalog.GpuPerf(gpuName);
            double? cpuPerf = HardwareCatalog.CpuPerf(_context.CpuName);
            var (ratio, text) = BottleneckAnalyzer.Balance(gpuPerf ?? 0, cpuPerf ?? 0);
            _balanceRatio = ratio;

            // Fora da tabela não há estimativa: a régua some, para o ponto no meio não parecer "equilibrado".
            bool known = gpuPerf > 0 && cpuPerf > 0;
            BnBalanceRuler.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
            BnCpuName.Text = HardwareNaming.ShortCpuName(_context.CpuName);
            BnGpuName.Text = HardwareNaming.ShortGpuName(gpuName);
            BnBalanceText.Text = known ? text : (cpuPerf > 0, gpuPerf > 0) switch
            {
                (false, false) => "Estimativa indisponível: nem o processador nem a placa de vídeo estão na tabela de desempenho relativo.",
                (false, true) => $"Estimativa indisponível: o processador{Named(BnCpuName.Text)} não está na tabela de desempenho relativo.",
                _ => $"Estimativa indisponível: a placa de vídeo{Named(BnGpuName.Text)} não está na tabela de desempenho relativo.",
            };
            PositionBalanceDot();

            static string Named(string name) => name.Length > 0 ? $" ({name})" : "";
        }

        internal void Tick()
        {
            // Sem a página à vista e sem sessão, nada é lido (o CoreBi fica aberto durante os jogos).
            if (_context == null || (!IsVisible && _session == null)) return;

            var frames = _context.Frames.GetForegroundStats();
            var sample = BottleneckAnalyzer.Sample(_context.Sensors, _context.PrimaryGpu, frames);
            var limiter = sample.Classify();

            if (IsVisible)
            {
                bool game = frames != null;
                bool verdict = game && limiter != Limiter.Unknown;
                BnGameLine.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
                // Só o nome do processo, de propósito (anticheats): ver FrameMonitor.ResolveProcessName.
                BnLiveGame.Text = game ? $"Medindo {frames!.ProcessName}" : "";
                string tone = verdict ? VerdictTone(limiter) : "";
                if (verdict)
                {
                    SetLiveTitle(BottleneckAnalyzer.Title(limiter), tone);
                    BnLiveAdvice.Text = BottleneckAnalyzer.Advice(limiter);
                }
                else
                {
                    var (title, advice) = game ? Unclassified(sample, frames!)
                        : ("Nenhum jogo em primeiro plano", "Abra um jogo e deixe-o em primeiro plano: o CoreBi mostra qual peça limita o FPS.");
                    SetLiveTitle(title, null);
                    BnLiveAdvice.Text = advice;
                }

                BnFrameStats.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
                if (game)
                {
                    BnFrameStats.ItemsSource = new[]
                    {
                        new RulerStat("Agora", $"{frames!.Fps:0}", "FPS", true, "Quadros entregues no último segundo.", "quadros por segundo"),
                        new RulerStat("1% low", $"{frames.Low1Fps:0}", "FPS", false, "O FPS dos quadros mais lentos (o pior 1 % dos últimos 10 s). Mostra as travadas.", "os quadros mais lentos"),
                    };
                }

                BnLiveCard.SetResourceReference(Border.BackgroundProperty, tone == "Danger" ? "DangerSoft" : tone == "Warn" ? "WarnSoft" : "Surface");
                BnLiveCard.SetResourceReference(Border.BorderBrushProperty, tone == "" ? "Line" : tone);

                SetPct(_gpu, sample.GpuLoad);
                SetPct(_cpu, sample.CpuMaxCore);
                _cpu.Sub = string.Join(", ", new[]
                {
                    sample.BusiestCore is { } core ? char.ToUpper(core[0], CultureInfo.CurrentCulture) + core[1..] : null,
                    sample.CpuTotal is float total ? $"total da CPU {total:0} %" : null,
                }.Where(x => x != null));
                SetPct(_vram, sample.VramPct);
                SetPct(_ram, sample.RamPct);
                SetTemp(_gpuTemp, sample.GpuTemp);
                _gpuTemp.Sub = sample.GpuThermalCap ? "O driver está reduzindo o clock pela temperatura" : "";
                if (sample.GpuThermalCap) _gpuTemp.Tone = "Danger";
                SetTemp(_cpuTemp, sample.CpuTemp);
            }

            if (_session != null)
            {
                _session.Add(sample);
                var elapsed = _session.Elapsed;
                BnSessionStatus.Text = frames != null
                    ? $"Medindo {frames.ProcessName}: {elapsed:m\\:ss} de {_session.Duration:m\\:ss}, {_session.Count} amostras"
                    : $"Aguardando um jogo em primeiro plano: {elapsed:m\\:ss} de {_session.Duration:m\\:ss}";
                BnSessionProgress.ScaleX = Math.Clamp(elapsed / _session.Duration, 0, 1);
                if (_session.Done) FinishSession();
            }
        }

        private void BnBalanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => PositionBalanceDot();

        /// <summary>Posição do marcador: escala logarítmica da razão GPU/CPU (0,4× … 2,5×), centro = equilibrado.</summary>
        private void PositionBalanceDot()
        {
            double w = BnBalanceCanvas.ActualWidth;
            double t = Math.Clamp((Math.Log(_balanceRatio) - Math.Log(0.4)) / (Math.Log(2.5) - Math.Log(0.4)), 0, 1);
            Canvas.SetLeft(BnBalanceDot, t * (w - BnBalanceDot.Width));
        }

        private void BtnBnSession_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null)
            {
                _session = null;
                Ui.SetRunning(BtnBnSession, false, "Iniciar análise");
                BtnBnSession.ToolTip = SessionTip;
                BnSessionBar.Visibility = Visibility.Collapsed;
                BnSessionStatus.Text = "Análise cancelada.";
                _context?.SyncFrameMonitoring();
                return;
            }

            int minutes = int.Parse(Ui.CheckedTag(BnDurationPanel) ?? "1", CultureInfo.InvariantCulture);
            _session = new BottleneckSession { Duration = TimeSpan.FromMinutes(minutes) };
            BnResult.Visibility = Visibility.Collapsed;
            Ui.SetRunning(BtnBnSession, true, "Iniciar análise", "Cancelar");
            BtnBnSession.ToolTip = null;
            BnSessionProgress.ScaleX = 0;
            BnSessionBar.Visibility = Visibility.Visible;
            BnSessionStatus.Text = "Aguardando um jogo em primeiro plano.";
            BnSessionEmpty.Visibility = Visibility.Visible;
            _context?.SyncFrameMonitoring();
        }

        private void FinishSession()
        {
            var sess = _session!;
            _session = null;
            Ui.SetRunning(BtnBnSession, false, "Iniciar análise");
            BtnBnSession.ToolTip = SessionTip;
            BnSessionBar.Visibility = Visibility.Collapsed;
            _context?.SyncFrameMonitoring();

            if (sess.Count == 0)
            {
                BnSessionStatus.Text = $"Análise concluída às {DateTime.Now:HH:mm} sem nenhuma amostra classificável: faltou o FPS de um jogo ou o uso da GPU e da CPU.";
                BnResult.Visibility = Visibility.Collapsed;
                BnSessionEmpty.Visibility = Visibility.Visible;
                return;
            }

            BnSessionStatus.Text = $"Análise concluída às {DateTime.Now:HH:mm}, com {sess.Count} amostras.";
            var main = sess.Main ?? Limiter.Unknown;
            BnResultTitle.Text = BottleneckAnalyzer.Title(main);
            var stats = new List<(string Label, string Value, string Unit)>();
            if (sess.AvgFps is double fps) stats.Add(("Média", $"{fps:0}", "FPS"));
            if (sess.Avg(x => x.GpuLoad) is double g) stats.Add(("Uso médio da GPU", $"{g:0}", "%"));
            if (sess.Avg(x => x.CpuTotal) is double c) stats.Add(("Uso médio da CPU", $"{c:0}", "%"));
            if (sess.Avg(x => x.CpuMaxCore) is double k) stats.Add(("Núcleo mais ocupado", $"{k:0}", "%"));
            if (sess.Max(x => x.VramPct) is double v) stats.Add(("VRAM máxima", $"{v:0}", "%"));
            if (sess.Max(x => x.GpuTemp) is double gt) stats.Add(("GPU mais quente", $"{gt:0}", "°C"));
            BnResultStats.ItemsSource = stats.Select((x, i) => new RulerStat(x.Label, x.Value, x.Unit, i == 0)).ToList();
            BnDistribution.ItemsSource = sess.Distribution()
                .Select(d => new LimitReading(BottleneckAnalyzer.Title(d.Limiter), LimiterChannel(d.Limiter))
                {
                    Value = $"{d.Pct:0} %",
                    Fraction = d.Pct / 100,
                })
                .ToList();
            BnResultAdvice.Text = BottleneckAnalyzer.Advice(main);
            BnResult.Visibility = Visibility.Visible;
            BnSessionEmpty.Visibility = Visibility.Collapsed;
        }

        private void SetLiveTitle(string text, string? tone)
        {
            bool verdict = tone != null;
            BnLiveTitle.Text = text;
            BnLiveTitle.FontSize = verdict ? 44 : 17;
            BnLiveTitle.SetResourceReference(TextBlock.ForegroundProperty, !verdict ? "Muted" : tone == "" ? "Fg" : tone!);
        }

        private static void SetPct(LimitReading reading, double? pct)
        {
            reading.Value = pct is double p ? $"{p:0} %" : "—";
            reading.Fraction = (pct ?? 0) / 100;
        }

        private static void SetTemp(LimitReading reading, float? temp)
        {
            reading.Value = temp is float t ? $"{t:0} °C" : "—";
            reading.Fraction = (temp ?? 0) / 100.0;
            reading.Tone = temp >= LoadSample.ThermalLimitC ? "Danger" : "";
        }

        private static (string Title, string Advice) Unclassified(LoadSample s, FrameStats f)
        {
            if (f.Fps is not > 0) return ("Sem quadros novos", "O programa em primeiro plano não entregou nenhum quadro no último segundo.");
            string missing = (s.GpuLoad == null, s.CpuTotal == null) switch
            {
                (true, true) => "o uso da GPU e da CPU",
                (true, false) => "o uso da GPU",
                _ => "o uso da CPU",
            };
            return ($"Falta {missing}", "O FPS chega, mas sem essa leitura não dá para dizer o que limita.");
        }

        // Só o que pede ação ganha cor; GPU no limite é o esperado num jogo e fica em tinta.
        private static string VerdictTone(Limiter l) => l switch
        {
            Limiter.Thermal => "Danger",
            Limiter.Cpu or Limiter.Vram or Limiter.Ram => "Warn",
            _ => "",
        };

        private static string LimiterChannel(Limiter l) => l switch
        {
            Limiter.Gpu or Limiter.Vram => "Gpu",
            Limiter.Cpu => "Cpu",
            Limiter.Ram => "Memory",
            Limiter.Thermal => "Danger",
            _ => "",
        };

        /// <summary>Abaixo de 1200 px o painel de leituras desce para baixo da análise de sessão, na largura toda.</summary>
        private void BnPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool narrow = BnPage.ActualWidth < 1200;
            BnSideColumn.Width = new GridLength(narrow ? 0 : 540);
            BnGapColumn.Width = new GridLength(narrow ? 0 : 16);
            BnStackGap.Height = new GridLength(narrow ? 16 : 0);
            Grid.SetColumn(BnSideCard, narrow ? 0 : 2);
            Grid.SetRow(BnSideCard, narrow ? 2 : 0);
        }
    }
}
