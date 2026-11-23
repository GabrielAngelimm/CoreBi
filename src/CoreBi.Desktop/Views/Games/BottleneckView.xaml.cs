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
    /// leitura que explica o veredito de agora. Na tela, só o nome, o número e o medidor: o limite e o detalhe da leitura (o
    /// núcleo, o aviso do driver, a falta do sensor) ficam na dica (Tip), para as seis linhas terem o mesmo desenho e pouco texto.
    /// </summary>
    public sealed class LimitReading : ObservableObject
    {
        private readonly string _tip;
        private string _label, _value = "—", _note = "", _tone = "";
        private double _fraction;
        private bool _active;

        public LimitReading(string label, string channel, double mark, string limit, string tip, bool first = false)
        {
            _label = label;
            Channel = channel;
            Mark = mark;
            _tip = $"{tip}\n{limit}.";
            First = first;
        }

        public bool First { get; }
        public string Channel { get; }
        public double Mark { get; }
        public string Tip => _note.Length > 0 ? $"{_note}\n\n{_tip}" : _tip;
        public bool HasMark => !double.IsNaN(Mark);
        public GridLength MarkLeft => new(HasMark ? Mark : 0, GridUnitType.Star);
        public GridLength MarkRight => new(HasMark ? 1 - Mark : 1, GridUnitType.Star);

        public string Label { get => _label; set => Set(ref _label, value); }
        public string Value { get => _value; set => Set(ref _value, value); }
        /// <summary>O detalhe da leitura agora, no começo da dica (vazio = só a explicação e o limite).</summary>
        public string Note
        {
            get => _note;
            set { if (Set(ref _note, value ?? "")) OnPropertyChanged(nameof(Tip)); }
        }
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
            Channel = channel;
            // O que a situação quer dizer e o que fazer nela: na dica, para a linha ficar só com o nome e o tempo
            Tip = $"{meaning}\n\n{tip}";
        }

        public Limiter Limiter { get; }
        public bool First { get; }
        public string Label { get; }
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
        private const string SessionTip = "Inicie e vá para o jogo: a análise só conta o tempo com ele em primeiro plano, então pode sair e voltar.";
        private const string IdleStatus = "Última análise";
        private const string LowTip = "O FPS dos quadros mais lentos (o pior 1 % dos últimos 10 s). Mostra as travadas que o FPS médio esconde.";
        private const string IdleTitle = "Nenhum jogo em primeiro plano";
        private const string IdleAdvice = "Abra um jogo: ao voltar dele, o resumo dos últimos 30 s fica aqui.";
        private const string IdleTip = "Num segundo monitor, o veredito aparece aqui ao vivo; com o jogo em tela cheia, ao voltar dele (Alt+Tab) fica o resumo dos últimos 30 segundos.";
        // Quanto de jogo o cartão guarda para mostrar quando o jogo sai da frente
        private static readonly TimeSpan HeldWindow = TimeSpan.FromSeconds(30);

        private FeatureContext? _context;
        private BottleneckSession? _session;
        private double _balanceRatio = 1;
        // Os últimos 30 s do jogo em primeiro plano (uma leitura por segundo): com tela cheia o CoreBi não aparece durante o jogo,
        // então, quando a pessoa volta (Alt+Tab), o cartão Agora e as leituras mostram o que aconteceu nesses segundos.
        private readonly Queue<(DateTime At, LoadSample Sample, Limiter Limiter)> _recent = new();
        private string? _recentGame;
        // Prévia das capturas de tela: um resultado e um veredito de exemplo que o Tick não sobrescreve (nada é salvo)
        private bool _demo;

        private readonly LimitReading _gpu = new("Uso da GPU", "Gpu", LoadSample.GpuLimitPct / 100.0, $"Limite em {LoadSample.GpuLimitPct:0} %",
            "Quanto a placa de vídeo está trabalhando. A partir de 90 % ela é o limite, o normal em jogos pesados.", first: true);
        private readonly LimitReading _cpu = new("Núcleo mais ocupado", "Cpu", LoadSample.CpuLimitPct / 100.0, $"Limite em {LoadSample.CpuLimitPct:0} %",
            "Jogos costumam depender de um ou dois núcleos. A partir de 85 % num núcleo (ou no total) o processador passa a segurar o FPS.");
        private readonly LimitReading _vram = new("VRAM em uso", "Gpu", LoadSample.VramLimitPct / 100.0, $"Limite em {LoadSample.VramLimitPct:0} %",
            "A memória da própria placa de vídeo, onde ficam as texturas. A partir de 97 % ela está cheia e o jogo engasga.");
        private readonly LimitReading _ram = new("RAM em uso", "Memory", LoadSample.RamLimitPct / 100.0, $"Limite em {LoadSample.RamLimitPct:0} %",
            "A memória do PC. A partir de 92 % o Windows passa a usar o disco no lugar dela, bem mais lento.");
        private readonly LimitReading _gpuTemp = new("Temperatura da GPU", "Gpu", LoadSample.ThermalLimitC / 100.0, $"Limite em {LoadSample.ThermalLimitC:0} °C",
            "A partir de 95 °C, ou quando o driver reduz o clock pela temperatura, o limite é o calor.");
        private readonly LimitReading _cpuTemp = new("Temperatura da CPU", "Cpu", LoadSample.ThermalLimitC / 100.0, $"Limite em {LoadSample.ThermalLimitC:0} °C",
            "A partir de 95 °C o processador reduz o clock para se proteger. Sem modo administrador, a leitura pode faltar.");

        private readonly SituationItem[] _situations = BottleneckAnalyzer.Situations
            .Select((l, i) => new SituationItem(l, i == 0, SituationName(l), BottleneckAnalyzer.Meaning(l), LimiterChannel(l), BottleneckAnalyzer.Advice(l)))
            .ToArray();

        private readonly SessionStat _avgFps = new("FPS médio", "FPS", true, "A média de quadros por segundo nos segundos medidos.");
        private readonly SessionStat _avgLow = new("1% low médio", "FPS", false, "A média do FPS dos quadros mais lentos: quanto mais perto do FPS médio, menos travadas.");

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
            BnResultStats.ItemsSource = new[] { _avgFps, _avgLow };
            ShowRun(context.Settings.BottleneckRuns.FirstOrDefault());
            SetLive(IdleTitle, IdleAdvice, null, IdleTip);
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
            Remember(sample, frames, limiter);

            if (IsVisible)
            {
                if (frames == null && Held() is { } held) ShowHeld(held);
                else ShowLive(sample, frames, limiter);
            }

            if (_session != null)
            {
                _session.Add(sample);
                var elapsed = _session.Measured;
                string clock = $"{Clock(elapsed)} de {Clock(_session.Duration)}";
                // Só o nome do processo, de propósito (anticheats): ver FrameMonitor.ResolveProcessName.
                BnSessionStatus.Text = frames is { Fps: > 0 }
                    ? $"Medindo {frames.ProcessName} · {clock}"
                    : elapsed > TimeSpan.Zero
                        ? $"Pausada fora do jogo · {clock}"
                        : $"Esperando o jogo em primeiro plano · {clock}";
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
            SetMode(held: false, game ? frames!.ProcessName : null, "");
            string? tone = verdict ? VerdictTone(limiter) : null;
            if (verdict) SetLive(BottleneckAnalyzer.Title(limiter), BottleneckAnalyzer.Hint(limiter), tone, BottleneckAnalyzer.Advice(limiter));
            else if (game)
            {
                var (title, advice) = Unclassified(sample, frames!);
                SetLive(title, advice, null, null);
            }
            else SetLive(IdleTitle, IdleAdvice, null, IdleTip);

            BnFpsNow.Text = frames?.Fps is > 0 ? $"{frames.Fps:0}" : "—";
            BnFpsLow.Text = frames?.Low1Fps is > 0 ? $"{frames.Low1Fps:0}" : "—";
            ShowReadings(sample, verdict ? limiter : null);
        }

        /// <summary>
        /// Fora do jogo: o resumo dos últimos segundos dele no cartão (a situação mais frequente, o FPS e o 1% low médios) e nas
        /// leituras (médias de uso, máximas de memória e temperatura), com há quanto tempo o jogo saiu da frente.
        /// </summary>
        private void ShowHeld((LoadSample Sample, Limiter Main, string Game, DateTime Last, int Seconds) held)
        {
            SetMode(held: true, held.Game, " · há " + ReadoutFormat.Span(DateTime.Now - held.Last));
            BnFpsCell.ToolTip = $"A média de quadros por segundo nos últimos {held.Seconds} s do jogo.";
            BnLowCell.ToolTip = $"A média do 1% low nos últimos {held.Seconds} s do jogo: quanto mais perto do FPS médio, menos travadas.";
            if (held.Main != Limiter.Unknown) SetLive(BottleneckAnalyzer.Title(held.Main), BottleneckAnalyzer.Hint(held.Main), VerdictTone(held.Main), BottleneckAnalyzer.Advice(held.Main));
            else SetLive("Sem veredito", "Faltou o uso da GPU ou da CPU para dizer qual peça limitava.", null, null);
            BnFpsNow.Text = held.Sample.Fps is > 0 ? $"{held.Sample.Fps:0}" : "—";
            BnFpsLow.Text = held.Sample.Low1Fps is > 0 ? $"{held.Sample.Low1Fps:0}" : "—";
            ShowReadings(held.Sample, held.Main == Limiter.Unknown ? null : held.Main);
        }

        /// <summary>Ao vivo ("Agora", LED aceso) ou guardado ("Último jogo", LED apagado e há quanto tempo).</summary>
        private void SetMode(bool held, string? game, string ago)
        {
            BnLiveLabel.Text = held ? "Último jogo" : "Agora";
            BnLiveDot.Visibility = game != null ? Visibility.Visible : Visibility.Collapsed;
            BnLiveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, held ? "Faint" : "Signal");
            BnLiveDot.ToolTip = held ? "O jogo saiu da frente: estes são os últimos segundos medidos" : "Medindo o FPS do programa em primeiro plano";
            BnLiveGame.Text = game ?? "";
            BnLiveAgo.Text = ago;
            BnFpsLabel.Text = held ? "FPS médio" : "FPS agora";
            if (!held)
            {
                BnFpsCell.ToolTip = "Quadros que o jogo entregou no último segundo.";
                BnLowCell.ToolTip = LowTip;
            }
            BnReadingsTitle.Text = held ? "Leituras do último jogo" : "Leituras";
        }

        /// <summary>Os seis medidores; a leitura que explica o veredito acende o rótulo.</summary>
        private void ShowReadings(LoadSample sample, Limiter? limiter)
        {
            SetPct(_gpu, sample.GpuLoad);
            SetPct(_cpu, sample.CpuMaxCore);
            _cpu.Note = string.Join(" · ", new[]
            {
                sample.BusiestCore is { Length: > 0 } core ? char.ToUpper(core[0], CultureInfo.CurrentCulture) + core[1..] : null,
                sample.CpuTotal is float total ? $"total da CPU {total:0} %" : null,
            }.Where(x => x != null));
            SetPct(_vram, sample.VramPct);
            SetPct(_ram, sample.RamPct);
            SetTemp(_gpuTemp, sample.GpuTemp);
            _gpuTemp.Note = sample.GpuThermalCap ? "O driver está reduzindo o clock pela temperatura." : "";
            if (sample.GpuThermalCap) _gpuTemp.Tone = "Danger";
            SetTemp(_cpuTemp, sample.CpuTemp);
            _cpuTemp.Note = sample.CpuTemp == null && _context?.SensorsRead == true
                ? CpuSensorMessages.Inline(_context.Sensors.DiagnoseCpuSensors(), "Sem leitura") ?? "" : "";

            // A leitura que explica o veredito acende o rótulo; as outras ficam no tom de sempre.
            var active = limiter switch
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

        /// <summary>Guarda a leitura com um jogo entregando quadros; outro jogo recomeça a janela.</summary>
        private void Remember(LoadSample sample, FrameStats? frames, Limiter limiter)
        {
            if (frames is not { Fps: > 0 }) return;
            if (!string.Equals(_recentGame, frames.ProcessName, StringComparison.OrdinalIgnoreCase)) _recent.Clear();
            _recentGame = frames.ProcessName;
            var now = DateTime.Now;
            _recent.Enqueue((now, sample, limiter));
            while (_recent.Count > 0 && now - _recent.Peek().At > HeldWindow) _recent.Dequeue();
        }

        /// <summary>
        /// O resumo dos últimos segundos do jogo: a situação mais frequente, médias de uso e de FPS, máximas de memória e de
        /// temperatura. Null sem nenhum jogo medido desde que o CoreBi abriu.
        /// </summary>
        private (LoadSample Sample, Limiter Main, string Game, DateTime Last, int Seconds)? Held()
        {
            if (_recent.Count == 0 || _recentGame == null) return null;
            var list = _recent.ToList();
            var samples = list.Select(x => x.Sample).ToList();
            float? Avg(Func<LoadSample, float?> f) => samples.Select(f).OfType<float>().DefaultIfEmpty(float.NaN).Average() is var a && float.IsFinite(a) ? a : null;
            float? Max(Func<LoadSample, float?> f) => samples.Select(f).OfType<float>().Cast<float?>().Max();
            double? AvgD(Func<LoadSample, double?> f) => samples.Select(f).OfType<double>().DefaultIfEmpty(double.NaN).Average() is var a && double.IsFinite(a) ? a : null;
            var classified = list.Where(x => x.Limiter != Limiter.Unknown).ToList();
            var main = classified.Count == 0 ? Limiter.Unknown : classified.GroupBy(x => x.Limiter).MaxBy(g => g.Count())!.Key;
            string? core = samples.Select(x => x.BusiestCore).Where(x => !string.IsNullOrEmpty(x)).GroupBy(x => x).MaxBy(g => g.Count())?.Key;
            var summary = new LoadSample(
                Avg(x => x.GpuLoad), Avg(x => x.CpuTotal), Avg(x => x.CpuMaxCore), core,
                samples.Select(x => x.VramPct).OfType<double>().Cast<double?>().Max(), Max(x => x.RamPct), Max(x => x.GpuTemp), Max(x => x.CpuTemp),
                samples.Any(x => x.GpuThermalCap), AvgD(x => x.Fps), AvgD(x => x.Low1Fps), _recentGame);
            return (summary, main, _recentGame, list[^1].At, Math.Clamp((int)Math.Round((list[^1].At - list[0].At).TotalSeconds) + 1, 1, (int)HeldWindow.TotalSeconds));
        }

        // O veredito, a linha do que fazer e, na dica dela, o conselho inteiro
        private void SetLive(string title, string hint, string? tone, string? tip)
        {
            BnLiveTitle.Text = title;
            BnLiveTitle.SetResourceReference(TextBlock.ForegroundProperty, tone == null ? "Muted" : tone == "" ? "Fg" : tone);
            BnLiveAdvice.Text = hint;
            BnLiveAdvice.ToolTip = tip;
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
                SetResult("Nenhuma ainda", "Escolha a duração, clique em Iniciar análise e vá para o jogo.", null, null);
                BnResult.ToolTip = null;
                foreach (var s in _situations) { s.Value = "—"; s.Fraction = 0; s.Quiet = false; }
                _avgFps.Value = _avgLow.Value = "—";
                if (status)
                {
                    BnSessionStatus.Text = IdleStatus;
                    BnSessionStatus.ToolTip = null;
                }
                return;
            }

            SetResult(BottleneckAnalyzer.Title(run.Main), BottleneckAnalyzer.Hint(run.Main), VerdictTone(run.Main), BottleneckAnalyzer.Advice(run.Main));
            foreach (var s in _situations)
            {
                double pct = run.Share(s.Limiter);
                s.Value = pct <= 0 ? "0 %" : pct < 1 ? "<1 %" : $"{pct:0} %";
                s.Fraction = pct / 100;
                s.Quiet = pct <= 0;
            }
            _avgFps.Value = Number(run.AvgFps);
            _avgLow.Value = Number(run.AvgLow1Fps);
            // Os outros números da análise ficam na dica do resultado, longe da leitura principal
            var more = new[]
            {
                run.GpuAvg is double g ? $"GPU média: {g:0} %" : null,
                run.CoreAvg is double c ? $"Núcleo mais ocupado, em média: {c:0} %" : null,
                run.VramMax is double v ? $"VRAM máxima: {v:0} %" : null,
                run.GpuTempMax is double t ? $"GPU mais quente: {t:0} °C" : null,
            }.OfType<string>().ToArray();
            BnResult.ToolTip = more.Length == 0 ? null : string.Join("\n", more);
            if (status)
            {
                BnSessionStatus.Text = Summary(run);
                BnSessionStatus.ToolTip = $"{Samples(run.Samples)}, uma por segundo de jogo.";
            }

            static string Number(double? v) => v is double d && double.IsFinite(d) ? $"{d:0}" : "—";
        }

        private void SetResult(string title, string hint, string? tone, string? tip)
        {
            BnResultTitle.Text = title;
            BnResultTitle.SetResourceReference(TextBlock.ForegroundProperty, tone == null ? "Muted" : tone == "" ? "Fg" : tone);
            BnResultAdvice.Text = hint;
            BnResultAdvice.ToolTip = tip;
        }

        // A linha de baixo do título: só quando e o quê (as amostras ficam na dica)
        private static string Summary(BottleneckRun run) => string.Join(" · ", new[]
        {
            "Última análise",
            run.Process.Length > 0 ? run.Process : null,
            ReadoutFormat.When(run.Date),
            ReadoutFormat.Span(TimeSpan.FromSeconds(run.Seconds)),
        }.Where(x => x != null));

        private static string Samples(int n) => n == 1 ? "1 amostra" : $"{n.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} amostras";

        private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        private void BnBalanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => PositionBalanceDot();

        /// <summary>
        /// Posição do marcador: escala logarítmica da razão GPU/CPU (0,4× … 2,5×), centro = equilibrado. O ponto vai para o lado da
        /// peça que tende a segurar o FPS (a CPU à esquerda, a placa de vídeo à direita), então os dois nomes bastam como legenda.
        /// </summary>
        private void PositionBalanceDot()
        {
            double w = BnBalanceCanvas.ActualWidth;
            double t = 1 - Math.Clamp((Math.Log(_balanceRatio) - Math.Log(0.4)) / (Math.Log(2.5) - Math.Log(0.4)), 0, 1);
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
                if (last == null) BnSessionStatus.Text = "Análise cancelada";
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
            SetResult("Medindo…", "O resultado se forma aqui enquanto você joga.", null, "A análise conta só os segundos com um jogo em primeiro plano: pode sair e voltar.");
            BnSessionStatus.Text = $"Esperando o jogo em primeiro plano · 0:00 de {Clock(_session.Duration)}";
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
                BnSessionStatus.Text = last == null ? "Análise sem amostras" : "Análise sem amostras · abaixo, a anterior";
                BnSessionStatus.ToolTip = "Faltou o FPS de um jogo em primeiro plano ou o uso da GPU e da CPU.";
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
                _recent.Clear();
                _recentGame = null;
                ShowRun(_context?.Settings.BottleneckRuns.FirstOrDefault());
                return;
            }

            // O caso comum: a pessoa voltou do jogo (Alt+Tab) e vê os últimos 30 s dele, saídos há 12 s
            _recent.Clear();
            _recentGame = "JogoDeExemplo";
            var rng = new Random(3);
            var end = DateTime.Now.AddSeconds(-12);
            for (int i = 29; i >= 0; i--)
            {
                float J(float v, float spread) => v + (float)((rng.NextDouble() - 0.5) * spread);
                var demo = new LoadSample(J(71, 6), J(46, 4), J(91, 5), "núcleo 3", J(64, 1), J(58, 1), J(66, 2), J(77, 2), false,
                                          J(118, 10), J(74, 6), _recentGame);
                _recent.Enqueue((end.AddSeconds(-i), demo, demo.Classify()));
            }
            ShowHeld(Held()!.Value);

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
            if (f.Fps is not > 0) return ("Sem quadros novos", "Jogo minimizado ou em carregamento.");
            string missing = (s.GpuLoad == null, s.CpuTotal == null) switch
            {
                (true, true) => "o uso da GPU e da CPU",
                (true, false) => "o uso da GPU",
                _ => "o uso da CPU",
            };
            return ($"Falta {missing}", "Sem essa leitura não dá para dizer qual peça limita.");
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
