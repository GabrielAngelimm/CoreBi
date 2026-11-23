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
    /// limite que a classificação usa (Mark, 0–1; NaN = sem marca). Atualizado no lugar a cada leitura; Active acende o rótulo da
    /// leitura que explica o veredito de agora.
    /// </summary>
    public sealed class LimitReading : ObservableObject
    {
        private string _label, _value = "—", _sub = "", _tone = "";
        private double _fraction;
        private bool _active;

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
        public bool Active { get => _active; set => Set(ref _active, value); }
        public double Fraction { get => _fraction; set => Set(ref _fraction, Math.Clamp(value, 0, 1)); }
    }

    /// <summary>Uma das seis situações da análise de sessão: fixa na tela, com o tempo nela atualizado no lugar.</summary>
    public sealed class SituationItem : ObservableObject
    {
        private string _value = "—";
        private double _fraction;
        private bool _quiet;

        public SituationItem(Limiter limiter, bool first, string label, string meaning, string channel, string tip)
        {
            Limiter = limiter;
            First = first;
            Label = label;
            Meaning = meaning;
            Channel = channel;
            Tip = tip;
        }

        public Limiter Limiter { get; }
        public bool First { get; }
        public string Label { get; }
        public string Meaning { get; }
        public string Channel { get; }
        public string Tip { get; }
        public string Value { get => _value; set => Set(ref _value, value); }
        public double Fraction { get => _fraction; set => Set(ref _fraction, Math.Clamp(value, 0, 1)); }
        /// <summary>A situação não aconteceu na análise mostrada (0 %): nome e número um tom abaixo.</summary>
        public bool Quiet { get => _quiet; set => Set(ref _quiet, value); }
    }

    /// <summary>Média ou máximo da sessão: número e unidade (a unidade some no travessão).</summary>
    public sealed class SessionStat : ObservableObject
    {
        private readonly string _unit;
        private string _value = "—";

        public SessionStat(string label, string unit, bool first, string tip)
        {
            Label = label;
            _unit = unit;
            First = first;
            Tip = tip;
        }

        public string Label { get; }
        public bool First { get; }
        public string Tip { get; }
        public string UnitText => _value == "—" ? "" : (_unit == "%" ? " %" : " " + _unit);

        public string Value
        {
            get => _value;
            set { if (Set(ref _value, value)) OnPropertyChanged(nameof(UnitText)); }
        }
    }
}

namespace CoreBi.Desktop.Views
{
    public partial class BottleneckView : UserControl
    {
        private const string SessionTip = "Inicie e vá para o jogo: a análise espera ele ficar em primeiro plano e mede pelo tempo escolhido.";
        private const string IdleStatus = "Jogue com o CoreBi aberto: no fim, você vê o que segurou o FPS e por quanto tempo.";
        private const string IdleTitle = "Nenhum jogo em primeiro plano";
        private const string IdleAdvice = "Abra um jogo e deixe-o em primeiro plano: aqui aparece, segundo a segundo, qual peça limita o FPS.";

        private FeatureContext? _context;
        private BottleneckSession? _session;
        private double _balanceRatio = 1;
        // Prévia das capturas de tela: um resultado e um veredito de exemplo que o Tick não sobrescreve (nada é salvo)
        private bool _demo;

        private readonly LimitReading _gpu = new("Uso da GPU", "Gpu", LoadSample.GpuLimitPct / 100.0,
            "Quanto a placa de vídeo está trabalhando. A partir de 90 % ela é o limite, o normal em jogos pesados.", "placa de vídeo");
        private readonly LimitReading _cpu = new("Núcleo mais ocupado", "Cpu", LoadSample.CpuLimitPct / 100.0,
            "Jogos costumam depender de um ou dois núcleos. A partir de 85 % num núcleo (ou no total) o processador passa a segurar o FPS.", "processador");
        private readonly LimitReading _vram = new("VRAM em uso", "Gpu", LoadSample.VramLimitPct / 100.0,
            "A memória da própria placa de vídeo, onde ficam as texturas. A partir de 97 % ela está cheia e o jogo engasga.", "memória da placa de vídeo");
        private readonly LimitReading _ram = new("RAM em uso", "Memory", LoadSample.RamLimitPct / 100.0,
            "A memória do PC. A partir de 92 % o Windows passa a usar o disco no lugar dela, bem mais lento.", "memória do PC");
        private readonly LimitReading _gpuTemp = new("Temperatura da GPU", "Gpu", LoadSample.ThermalLimitC / 100.0,
            "A partir de 95 °C, ou quando o driver reduz o clock pela temperatura, o limite é o calor.");
        private readonly LimitReading _cpuTemp = new("Temperatura da CPU", "Cpu", LoadSample.ThermalLimitC / 100.0,
            "A partir de 95 °C o processador reduz o clock para se proteger. Sem modo administrador, a leitura pode faltar.");

        private readonly SituationItem[] _situations = BottleneckAnalyzer.Situations
            .Select((l, i) => new SituationItem(l, i == 0, SituationName(l), BottleneckAnalyzer.Meaning(l), LimiterChannel(l), BottleneckAnalyzer.Advice(l)))
            .ToArray();

        private readonly SessionStat _avgFps = new("FPS médio", "FPS", true, "A média de quadros por segundo nos segundos medidos.");
        private readonly SessionStat _avgLow = new("1% low médio", "FPS", false, "A média do FPS dos quadros mais lentos: quanto mais perto do FPS médio, menos travadas.");
        private readonly SessionStat _gpuAvg = new("GPU média", "%", false, "O uso médio da placa de vídeo. Perto de 100 % ela foi bem aproveitada.");
        private readonly SessionStat _coreAvg = new("Núcleo mais ocupado", "%", false, "A média do núcleo do processador mais ocupado a cada segundo.");
        private readonly SessionStat _vramMax = new("VRAM máxima", "%", false, "O máximo de memória da placa de vídeo usado na análise.");
        private readonly SessionStat _gpuTempMax = new("GPU mais quente", "°C", false, "A maior temperatura da placa de vídeo na análise.");

        private readonly LimitReading[] _meters;

        public BottleneckView()
        {
            InitializeComponent();
            _meters = [_gpu, _cpu, _vram, _ram, _gpuTemp, _cpuTemp];
        }

        internal bool HasActiveSession => _session is { Done: false };

        internal void Initialize(FeatureContext context)
        {
            _context = context;
            BnMeters.ItemsSource = _meters;
            BnDistribution.ItemsSource = _situations;
            BnResultStats.ItemsSource = new[] { _avgFps, _avgLow, _gpuAvg, _coreAvg, _vramMax, _gpuTempMax };
            ShowRun(context.Settings.BottleneckRuns.FirstOrDefault());
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
            if (_context == null || _demo || (!IsVisible && _session == null)) return;

            var frames = _context.Frames.GetForegroundStats();
            var sample = BottleneckAnalyzer.Sample(_context.Sensors, _context.PrimaryGpu, frames);
            var limiter = sample.Classify();

            if (IsVisible) ShowLive(sample, frames, limiter);

            if (_session != null)
            {
                _session.Add(sample);
                var elapsed = _session.Elapsed;
                string clock = $"{Clock(elapsed)} de {Clock(_session.Duration)}";
                // Só o nome do processo, de propósito (anticheats): ver FrameMonitor.ResolveProcessName.
                BnSessionStatus.Text = frames != null
                    ? $"Medindo {frames.ProcessName} · {clock} · {Samples(_session.Count)}"
                    : $"Vá para o jogo e deixe-o em primeiro plano · {clock}";
                Ui.Glide(BnSessionProgress, Math.Clamp(elapsed / _session.Duration, 0, 1), _context.IsLiveMotion());
                if (_session.ToRun(DateTime.Now) is { } partial) ShowRun(partial, status: false);
                if (_session.Done) FinishSession();
            }
        }

        /// <summary>O cartão Agora e as leituras: o veredito do último segundo, o FPS e o que prova o veredito.</summary>
        private void ShowLive(LoadSample sample, FrameStats? frames, Limiter limiter)
        {
            bool game = frames != null;
            bool verdict = game && limiter != Limiter.Unknown;
            BnLiveDot.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
            BnLiveGame.Text = game ? frames!.ProcessName : "";
            string? tone = verdict ? VerdictTone(limiter) : null;
            if (verdict) SetLive(BottleneckAnalyzer.Title(limiter), BottleneckAnalyzer.Advice(limiter), tone);
            else if (game)
            {
                var (title, advice) = Unclassified(sample, frames!);
                SetLive(title, advice, null);
            }
            else SetLive(IdleTitle, IdleAdvice, null);

            BnFpsNow.Text = frames?.Fps is > 0 ? $"{frames.Fps:0}" : "—";
            BnFpsLow.Text = frames?.Low1Fps is > 0 ? $"{frames.Low1Fps:0}" : "—";

            SetPct(_gpu, sample.GpuLoad);
            SetPct(_cpu, sample.CpuMaxCore);
            _cpu.Sub = string.Join(" · ", new[]
            {
                sample.BusiestCore is { Length: > 0 } core ? char.ToUpper(core[0], CultureInfo.CurrentCulture) + core[1..] : null,
                sample.CpuTotal is float total ? $"total da CPU {total:0} %" : null,
            }.Where(x => x != null));
            SetPct(_vram, sample.VramPct);
            SetPct(_ram, sample.RamPct);
            SetTemp(_gpuTemp, sample.GpuTemp);
            _gpuTemp.Sub = sample.GpuThermalCap ? "O driver está reduzindo o clock pela temperatura" : "";
            if (sample.GpuThermalCap) _gpuTemp.Tone = "Danger";
            SetTemp(_cpuTemp, sample.CpuTemp);
            _cpuTemp.Sub = sample.CpuTemp == null && _context?.SensorsRead == true
                ? CpuSensorMessages.Inline(_context.Sensors.DiagnoseCpuSensors(), "Sem leitura") ?? "" : "";

            // A leitura que explica o veredito acende o rótulo; as outras ficam no tom de sempre.
            var active = !verdict ? null : limiter switch
            {
                Limiter.Gpu => _gpu,
                Limiter.Cpu => _cpu,
                Limiter.Vram => _vram,
                Limiter.Ram => _ram,
                Limiter.Thermal => sample.GpuThermalCap || sample.GpuTemp >= LoadSample.ThermalLimitC ? _gpuTemp : _cpuTemp,
                _ => null,
            };
            foreach (var m in _meters) m.Active = ReferenceEquals(m, active);
        }

        private void SetLive(string title, string advice, string? tone)
        {
            BnLiveTitle.Text = title;
            BnLiveTitle.SetResourceReference(TextBlock.ForegroundProperty, tone == null ? "Muted" : tone == "" ? "Fg" : tone);
            BnLiveAdvice.Text = advice;
            BnLiveCard.SetResourceReference(Border.BackgroundProperty, tone == "Danger" ? "DangerSoft" : tone == "Warn" ? "WarnSoft" : "Surface");
            BnLiveCard.SetResourceReference(Border.BorderBrushProperty, string.IsNullOrEmpty(tone) ? "Line" : tone);
        }

        /// <summary>
        /// Mostra uma análise (a última guardada, a que acabou de terminar ou a parcial durante a medição) no lugar: as situações e
        /// as médias deslizam até os números novos. Sem análise, travessões e medidores vazios.
        /// </summary>
        private void ShowRun(BottleneckRun? run, bool status = true)
        {
            if (run == null)
            {
                SetResult("Nenhuma análise ainda", "Escolha a duração, clique em Iniciar análise e vá jogar. Ao voltar, o resultado está aqui.", null);
                foreach (var s in _situations) { s.Value = "—"; s.Fraction = 0; s.Quiet = false; }
                foreach (var stat in new[] { _avgFps, _avgLow, _gpuAvg, _coreAvg, _vramMax, _gpuTempMax }) stat.Value = "—";
                if (status) BnSessionStatus.Text = IdleStatus;
                return;
            }

            SetResult(BottleneckAnalyzer.Title(run.Main), BottleneckAnalyzer.Advice(run.Main), VerdictTone(run.Main));
            foreach (var s in _situations)
            {
                double pct = run.Share(s.Limiter);
                s.Value = pct <= 0 ? "0 %" : pct < 1 ? "<1 %" : $"{pct:0} %";
                s.Fraction = pct / 100;
                s.Quiet = pct <= 0;
            }
            _avgFps.Value = Number(run.AvgFps);
            _avgLow.Value = Number(run.AvgLow1Fps);
            _gpuAvg.Value = Number(run.GpuAvg);
            _coreAvg.Value = Number(run.CoreAvg);
            _vramMax.Value = Number(run.VramMax);
            _gpuTempMax.Value = Number(run.GpuTempMax);
            if (status) BnSessionStatus.Text = Summary(run);

            static string Number(double? v) => v is double d && double.IsFinite(d) ? $"{d:0}" : "—";
        }

        private void SetResult(string title, string advice, string? tone)
        {
            BnResultTitle.Text = title;
            BnResultTitle.SetResourceReference(TextBlock.ForegroundProperty, tone == null ? "Muted" : tone == "" ? "Fg" : tone);
            BnResultAdvice.Text = advice;
            BnResultAdvice.SetResourceReference(TextBlock.ForegroundProperty, tone == null ? "Muted" : "Fg");
        }

        private static string Summary(BottleneckRun run) => string.Join(" · ", new[]
        {
            "Última análise",
            run.Process.Length > 0 ? run.Process : null,
            ReadoutFormat.When(run.Date),
            ReadoutFormat.Span(TimeSpan.FromSeconds(run.Seconds)),
            Samples(run.Samples),
        }.Where(x => x != null));

        private static string Samples(int n) => n == 1 ? "1 amostra" : $"{n.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} amostras";

        private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

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
                StopUi();
                var last = _context?.Settings.BottleneckRuns.FirstOrDefault();
                ShowRun(last);
                BnSessionStatus.Text = last == null ? "Análise cancelada." : "Análise cancelada · " + Summary(last);
                return;
            }

            int minutes = int.Parse(Ui.CheckedTag(BnDurationPanel) ?? "1", CultureInfo.InvariantCulture);
            _session = new BottleneckSession { Duration = TimeSpan.FromMinutes(minutes) };
            Ui.SetRunning(BtnBnSession, true, "Iniciar análise", "Cancelar");
            BtnBnSession.ToolTip = null;
            BnDurationPanel.IsEnabled = false;
            BnSessionProgress.ScaleX = 0;
            BnSessionBar.Opacity = 1;
            ShowRun(null, status: false);
            SetResult("Medindo…", "A análise conta só os segundos com um jogo em primeiro plano. O resultado se forma aqui enquanto você joga.", null);
            BnSessionStatus.Text = $"Vá para o jogo e deixe-o em primeiro plano · 0:00 de {Clock(_session.Duration)}";
            _context?.SyncFrameMonitoring();
        }

        private void StopUi()
        {
            Ui.SetRunning(BtnBnSession, false, "Iniciar análise");
            BtnBnSession.ToolTip = SessionTip;
            BnDurationPanel.IsEnabled = true;
            BnSessionBar.Opacity = 0;
            _context?.SyncFrameMonitoring();
        }

        private void FinishSession()
        {
            var sess = _session!;
            _session = null;
            StopUi();

            if (sess.ToRun(DateTime.Now) is not { } run)
            {
                var last = _context?.Settings.BottleneckRuns.FirstOrDefault();
                ShowRun(last);
                BnSessionStatus.Text = "A análise terminou sem nenhuma amostra: faltou o FPS de um jogo em primeiro plano ou o uso da GPU e da CPU."
                    + (last == null ? "" : " Abaixo, a anterior.");
                return;
            }

            ShowRun(run);
            if (_context == null) return;
            var runs = _context.Settings.BottleneckRuns;
            runs.Insert(0, run);
            if (runs.Count > BottleneckRun.Capacity) runs.RemoveRange(BottleneckRun.Capacity, runs.Count - BottleneckRun.Capacity);
            _context.SaveSettings();
            _context.RecordEvent("", "Análise de gargalo", $"{BottleneckAnalyzer.Title(run.Main)} em {run.Share(run.Main):0} % do tempo",
                run.AvgFps is double fps ? $"{fps:0} FPS" : "", LimiterChannel(run.Main));
        }

        /// <summary>
        /// Prévia para as capturas de tela: um veredito ao vivo e uma análise de exemplo, sem medir nem salvar nada. false volta à
        /// página real (a última análise guardada e o Tick de sempre).
        /// </summary>
        internal void PreviewDemo(bool on)
        {
            _demo = on;
            if (!on)
            {
                ShowRun(_context?.Settings.BottleneckRuns.FirstOrDefault());
                return;
            }

            BnLiveDot.Visibility = Visibility.Visible;
            BnLiveGame.Text = "JogoDeExemplo";
            SetLive(BottleneckAnalyzer.Title(Limiter.Cpu), BottleneckAnalyzer.Advice(Limiter.Cpu), VerdictTone(Limiter.Cpu));
            BnFpsNow.Text = "118";
            BnFpsLow.Text = "74";
            (double, string)[] values = [(0.71, "71 %"), (0.91, "91 %"), (0.64, "64 %"), (0.58, "58 %"), (0.67, "67 °C"), (0.78, "78 °C")];
            for (int i = 0; i < _meters.Length; i++)
            {
                _meters[i].Fraction = values[i].Item1;
                _meters[i].Value = values[i].Item2;
                _meters[i].Tone = "";
                _meters[i].Sub = "";
                _meters[i].Active = _meters[i] == _cpu;
            }
            _cpu.Sub = "Núcleo 3 · total da CPU 46 %";

            BnSessionStatus.Text = Summary(new BottleneckRun { Process = "JogoDeExemplo", Date = DateTime.Now.AddMinutes(-12), Seconds = 180, Samples = 176 });
            ShowRun(new BottleneckRun
            {
                Date = DateTime.Now.AddMinutes(-12),
                Process = "JogoDeExemplo",
                Seconds = 180,
                Samples = 176,
                Main = Limiter.Gpu,
                Shares = [new(Limiter.Gpu, 64.2), new(Limiter.Cpu, 27.8), new(Limiter.Capped, 6.3), new(Limiter.Vram, 1.7)],
                AvgFps = 112,
                AvgLow1Fps = 71,
                GpuAvg = 92,
                CoreAvg = 83,
                VramMax = 89,
                GpuTempMax = 74,
            }, status: false);
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
            if (f.Fps is not > 0) return ("Sem quadros novos", "O programa em primeiro plano não entregou nenhum quadro no último segundo (jogo minimizado ou em carregamento).");
            string missing = (s.GpuLoad == null, s.CpuTotal == null) switch
            {
                (true, true) => "o uso da GPU e da CPU",
                (true, false) => "o uso da GPU",
                _ => "o uso da CPU",
            };
            return ($"Falta {missing}", "O FPS chega, mas sem essa leitura não dá para dizer qual peça limita.");
        }

        // O nome da situação na lista da análise (o título do veredito, menos "Nada no limite", que lá vira o motivo provável).
        private static string SituationName(Limiter l) => l == Limiter.Capped ? "Trava de FPS ou menu" : BottleneckAnalyzer.Title(l);

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
