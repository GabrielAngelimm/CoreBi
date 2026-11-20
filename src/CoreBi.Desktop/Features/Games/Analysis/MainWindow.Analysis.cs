using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using CoreBi.Engine.Games;

namespace CoreBi.Desktop;

/// <summary>
/// Medidor do Gargalo: leitura, fração 0–1 na cor do componente (Channel: Cpu, Gpu, Memory, Danger; vazio = aço) e a marca do
/// limite que a classificação usa (Mark, 0–1; NaN = sem marca). Atualizado no lugar a cada leitura.
/// </summary>
public sealed class LimitReading : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public LimitReading(string label, string channel, double mark = double.NaN, string? tip = null)
    {
        _label = label; Channel = channel; Mark = mark; Tip = tip;
    }

    private string _label, _value = "—", _sub = "", _tone = "";
    private double _fraction;

    public string Channel { get; }
    public double Mark { get; }
    public string? Tip { get; }
    public bool HasMark => !double.IsNaN(Mark);
    public GridLength MarkLeft => new(HasMark ? Mark : 0, GridUnitType.Star);
    public GridLength MarkRight => new(HasMark ? 1 - Mark : 1, GridUnitType.Star);

    public string Label { get => _label; set => Set(ref _label, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Sub { get => _sub; set => Set(ref _sub, value); }
    /// <summary>"" normal; "Warn" ou "Danger" quando a leitura pede atenção.</summary>
    public string Tone { get => _tone; set => Set(ref _tone, value); }
    public double Fraction { get => _fraction; set => Set(ref _fraction, Math.Clamp(value, 0, 1)); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}

/// <summary>Leitura em régua (FPS, médias da sessão): rótulo, número e unidade; a primeira sem fio.</summary>
public sealed record RulerStat(string Label, string Value, string Unit, bool First);

public partial class MainWindow
{
    // Gargalo + overlay
    public sealed record MeterItem(string Label, string Value, double Fraction);
    internal readonly FrameMonitor _frames = new();
    internal BottleneckSession? _bnSession;
    internal double _balanceRatio = 1;
    private bool _balanceKnown;

    // Os limites são os de LoadSample.Classify: a marca mostra onde cada leitura passa a ser "no limite".
    private readonly LimitReading _bnGpu = new("Uso da GPU", "Gpu", 0.90, "A partir de 90 % a placa de vídeo é o limite (o normal em jogos pesados).");
    private readonly LimitReading _bnCpu = new("Núcleo mais ocupado da CPU", "Cpu", 0.85, "A partir de 85 % num núcleo (ou no total) a CPU passa a limitar o FPS.");
    private readonly LimitReading _bnVram = new("VRAM em uso", "Gpu", 0.97, "A partir de 97 % a memória de vídeo está cheia.");
    private readonly LimitReading _bnRam = new("RAM em uso", "Memory", 0.92, "A partir de 92 % a memória RAM está cheia.");
    private readonly LimitReading _bnGpuTemp = new("Temperatura da GPU", "Gpu", 0.95, "A partir de 95 °C, ou quando o driver reduz o clock pela temperatura, o limite é térmico.");
    private readonly LimitReading _bnCpuTemp = new("Temperatura da CPU", "Cpu", 0.95, "A partir de 95 °C o limite é térmico.");

    // ---------------- Gargalo ----------------

    internal void InitBottleneckAndOverlay()
    {
        string gpuName = _gpus.FirstOrDefault()?.Name ?? "";
        double? gpuPerf = HardwareCatalog.GpuPerf(gpuName), cpuPerf = HardwareCatalog.CpuPerf(_cpuName);
        var (ratio, text) = BottleneckAnalyzer.Balance(gpuPerf ?? 0, cpuPerf ?? 0);
        _balanceRatio = ratio;
        // Fora da tabela não há estimativa: a régua some, para o ponto no meio não parecer "equilibrado".
        _balanceKnown = gpuPerf > 0 && cpuPerf > 0;
        PageBottleneck.BnBalanceRuler.Visibility = _balanceKnown ? Visibility.Visible : Visibility.Collapsed;
        BnCpuName.Text = HardwareCatalog.ShortCpuName(_cpuName);
        BnGpuName.Text = HardwareCatalog.ShortGpuName(gpuName);
        BnBalanceText.Text = text;
        BnMeters.ItemsSource = new[] { _bnGpu, _bnCpu, _bnVram, _bnRam, _bnGpuTemp, _bnCpuTemp };

        // Preferências do overlay
        foreach (var rb in OvCornerPanel.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == _settings.OverlayCorner;
        // O tamanho salvo vai para a opção mais próxima (os três tamanhos já mudaram de valor uma vez).
        var scales = OvScalePanel.Children.OfType<RadioButton>().ToList();
        var nearest = scales.MinBy(rb => Math.Abs(double.Parse((string)rb.Tag, CultureInfo.InvariantCulture) - _settings.OverlayScale));
        foreach (var rb in scales) rb.IsChecked = rb == nearest;
        BuildOverlayPicker();
        PageOverlay.LayoutPreview(_settings.OverlayCorner, _settings.OverlayScale);
        PageOverlay.SetOpacity(_settings.OverlayOpacity);
        _overlayReady = true;
        UpdateOverlayStatus(null);
    }

    internal void UpdateFrameMonitoring()
    {
        if (_features == null || _features.IsClosing) return;
        bool needed = PageBottleneck.IsVisible || ChkOverlay.IsChecked == true || _bnSession is { Done: false };
        if (needed) _frames.Start();
        else if (_frames.Running) _frames.Stop();
    }

    /// <summary>Posição do marcador: escala logarítmica da razão GPU/CPU (0,4× … 2,5×), centro = equilibrado.</summary>
    internal void BnBalanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = BnBalanceCanvas.ActualWidth;
        double t = Math.Clamp((Math.Log(_balanceRatio) - Math.Log(0.4)) / (Math.Log(2.5) - Math.Log(0.4)), 0, 1);
        Canvas.SetLeft(BnBalanceDot, t * (w - BnBalanceDot.Width));
    }

    private static void SetPct(LimitReading r, double? pct)
    {
        r.Value = pct is double p ? $"{p:0} %" : "—";
        r.Fraction = (pct ?? 0) / 100;
    }

    private static void SetTemp(LimitReading r, float? temp)
    {
        r.Value = temp is float t ? $"{t:0} °C" : "—";
        r.Fraction = (temp ?? 0) / 100.0;
        r.Tone = temp >= 95 ? "Danger" : "";
    }

    /// <summary>Tom do veredito: só o que pede ação ganha cor; GPU no limite é o esperado num jogo e fica em tinta.</summary>
    private static string VerdictTone(Limiter l) => l switch
    {
        Limiter.Thermal => "Danger",
        Limiter.Cpu or Limiter.Vram or Limiter.Ram => "Warn",
        _ => ""
    };

    internal void UpdateBottleneck()
    {
        // Sem a página à vista e sem sessão, nada é lido (o CoreBi fica aberto durante os jogos).
        if (!PageBottleneck.IsVisible && _bnSession == null) return;
        var frames = _frames.GetForegroundStats();
        var s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), frames);
        var lim = s.Classify();

        if (PageBottleneck.IsVisible)
        {
            bool game = frames != null;
            PageBottleneck.BnGameLine.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
            BnLiveGame.Text = game ? $"Medindo {frames!.ProcessName}" : "";
            BnLiveTitle.Text = game ? BottleneckAnalyzer.Title(lim) : "Nenhum jogo em primeiro plano";
            BnLiveAdvice.Text = game ? BottleneckAnalyzer.Advice(lim) : "Abra um jogo e deixe-o em primeiro plano. O veredito aparece quando houver FPS.";
            PageBottleneck.BnFrameStats.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
            if (game)
                PageBottleneck.BnFrameStats.ItemsSource = new[]
                {
                    new RulerStat("FPS", $"{frames!.Fps:0}", "", true),
                    new RulerStat("1% low", $"{frames.Low1Fps:0}", "FPS", false),
                };

            string tone = game ? VerdictTone(lim) : "";
            BnLiveCard.SetResourceReference(Border.BackgroundProperty, tone == "Danger" ? "DangerSoft" : tone == "Warn" ? "WarnSoft" : "Surface");
            BnLiveCard.SetResourceReference(Border.BorderBrushProperty, tone == "" ? "Line" : tone);
            BnLiveTitle.SetResourceReference(TextBlock.ForegroundProperty, tone == "" ? "Fg" : tone);

            SetPct(_bnGpu, s.GpuLoad);
            SetPct(_bnCpu, s.CpuMaxCore);
            _bnCpu.Sub = string.Join(", ", new[]
            {
                s.BusiestCore is { } core ? char.ToUpper(core[0], CultureInfo.CurrentCulture) + core[1..] : null,
                s.CpuTotal is float total ? $"total da CPU {total:0} %" : null,
            }.Where(x => x != null));
            SetPct(_bnVram, s.VramPct);
            SetPct(_bnRam, s.RamPct);
            SetTemp(_bnGpuTemp, s.GpuTemp);
            _bnGpuTemp.Sub = s.GpuThermalCap ? "O driver está reduzindo o clock pela temperatura" : "";
            if (s.GpuThermalCap) _bnGpuTemp.Tone = "Danger";
            SetTemp(_bnCpuTemp, s.CpuTemp);
        }

        if (_bnSession != null)
        {
            _bnSession.Add(s);
            var el = DateTime.Now - _bnSession.Start;
            BnSessionStatus.Text = frames != null
                ? $"Medindo {frames.ProcessName}: {el:m\\:ss} de {_bnSession.Duration:m\\:ss}, {_bnSession.Count} amostras"
                : $"Aguardando um jogo em primeiro plano: {el:m\\:ss} de {_bnSession.Duration:m\\:ss}";
            PageBottleneck.BnSessionProgress.ScaleX = Math.Clamp(el / _bnSession.Duration, 0, 1);
            if (_bnSession.Done) FinishBottleneckSession();
        }
    }

    internal void BtnBnSession_Click(object sender, RoutedEventArgs e)
    {
        if (_bnSession != null)
        {
            _bnSession = null;
            Ui.SetRunning(BtnBnSession, false, "Iniciar análise");
            PageBottleneck.BnSessionBar.Visibility = Visibility.Collapsed;
            BnSessionStatus.Text = "Análise cancelada.";
            return;
        }
        int minutes = int.Parse(CheckedTag(BnDurationPanel) ?? "1");
        _bnSession = new BottleneckSession { Duration = TimeSpan.FromMinutes(minutes) };
        BnResult.Visibility = Visibility.Collapsed;
        Ui.SetRunning(BtnBnSession, true, "Iniciar análise", "Cancelar");
        PageBottleneck.BnSessionProgress.ScaleX = 0;
        PageBottleneck.BnSessionBar.Visibility = Visibility.Visible;
        BnSessionStatus.Text = "Aguardando um jogo em primeiro plano.";
    }

    /// <summary>Canal de cor de cada situação na distribuição do tempo: a peça que limitou (térmico é veredito, nada no limite é aço).</summary>
    private static string LimiterChannel(Limiter l) => l switch
    {
        Limiter.Gpu or Limiter.Vram => "Gpu",
        Limiter.Cpu => "Cpu",
        Limiter.Ram => "Memory",
        Limiter.Thermal => "Danger",
        _ => ""
    };

    internal void FinishBottleneckSession()
    {
        var sess = _bnSession!;
        _bnSession = null;
        Ui.SetRunning(BtnBnSession, false, "Iniciar análise");
        PageBottleneck.BnSessionBar.Visibility = Visibility.Collapsed;
        BnSessionStatus.Text = sess.Count > 0
            ? $"Análise concluída às {DateTime.Now:HH:mm}, com {sess.Count} amostras."
            : $"Análise concluída às {DateTime.Now:HH:mm}, sem nenhuma amostra com jogo em primeiro plano.";

        var main = sess.Main ?? Limiter.Unknown;
        BnResultTitle.Text = BottleneckAnalyzer.Title(main);
        var stats = new List<(string Label, string Value, string Unit)>();
        if (sess.AvgFps is double fps) stats.Add(("FPS médio", $"{fps:0}", ""));
        if (sess.Avg(x => x.GpuLoad) is double g) stats.Add(("Uso médio da GPU", $"{g:0}", "%"));
        if (sess.Avg(x => x.CpuTotal) is double c) stats.Add(("Uso médio da CPU", $"{c:0}", "%"));
        if (sess.Avg(x => x.CpuMaxCore) is double k) stats.Add(("Núcleo mais ocupado", $"{k:0}", "%"));
        if (sess.Max(x => x.VramPct) is double v) stats.Add(("VRAM máxima", $"{v:0}", "%"));
        if (sess.Max(x => x.GpuTemp) is double gt) stats.Add(("GPU mais quente", $"{gt:0}", "°C"));
        PageBottleneck.BnResultStats.ItemsSource = stats.Select((x, i) => new RulerStat(x.Label, x.Value, x.Unit, i == 0)).ToList();
        BnDistribution.ItemsSource = sess.Distribution()
            .Select(d => new LimitReading(BottleneckAnalyzer.Title(d.Limiter), LimiterChannel(d.Limiter)) { Value = $"{d.Pct:0} %", Fraction = d.Pct / 100 })
            .ToList();
        BnResultAdvice.Text = BottleneckAnalyzer.Advice(main);
        BnResult.Visibility = Visibility.Visible;
    }
}
