using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop;

/// <summary>
/// Movimento da barra de ações do cabeçalho, sem nenhuma animação contínua:
/// <list type="bullet">
/// <item>o estado (<see cref="SoftTextProperty"/>) acende de leve (60 % a 100 % em 220 ms) quando o texto muda, em vez de trocar seco;</item>
/// <item>o medidor de progresso (TaskMeter) abre a largura e acende ao começar (260 ms), a barra desliza até cada fração nova
/// e a porcentagem conta junto com ela, quadro a quadro; ao chegar a 100 % ele fica cheio por um instante e então apaga e
/// fecha a largura (300 ms), sem empurrar os botões, que ficam rentes à direita. Uma etapa nova que chega no meio da abertura não
/// reinicia nada, e uma tarefa nova que começa enquanto ele fecha o faz voltar dali mesmo, sem saltar;</item>
/// <item>um botão que troca de texto ou de cor (<see cref="Soften"/>) acende em 180 ms;</item>
/// <item>o estado de uma tarefa (<see cref="Say"/>) passa da mensagem intermediária para a final numa troca suave, e a intermediária
/// fica o bastante na tela para ser lida.</item>
/// </list>
/// Com "Mostrar animações no Windows" desligado tudo vai direto ao valor final.
/// </summary>
public static class HeaderMotion
{
    private static readonly TimeSpan Open = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan Slide = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan Close = TimeSpan.FromMilliseconds(300);
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    private static bool Animate => SystemParameters.ClientAreaAnimation;

    private static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

    // ---------------- Estado que acende ao mudar ----------------

    public static readonly DependencyProperty SoftTextProperty = DependencyProperty.RegisterAttached(
        "SoftText", typeof(bool), typeof(HeaderMotion), new PropertyMetadata(false, OnSoftTextChanged));
    public static bool GetSoftText(DependencyObject d) => (bool)d.GetValue(SoftTextProperty);
    public static void SetSoftText(DependencyObject d, bool value) => d.SetValue(SoftTextProperty, value);

    private static readonly System.ComponentModel.DependencyPropertyDescriptor TextDescriptor =
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    private static void OnSoftTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text) return;
        if ((bool)e.NewValue) TextDescriptor.AddValueChanged(text, TextChanged);
        else TextDescriptor.RemoveValueChanged(text, TextChanged);
    }

    private static readonly DependencyProperty SoftAtProperty = DependencyProperty.RegisterAttached(
        "SoftAt", typeof(long), typeof(HeaderMotion), new PropertyMetadata(0L));

    private static void TextChanged(object? sender, EventArgs e)
    {
        if (_quiet || sender is not TextBlock text || string.IsNullOrEmpty(text.Text) || !Animate || !text.IsVisible) return;
        // Texto que muda várias vezes por segundo (o andamento do teste de rede) não acende a cada troca: piscaria
        long now = Environment.TickCount64;
        long last = (long)text.GetValue(SoftAtProperty);
        text.SetValue(SoftAtProperty, now);
        if (now - last < 700) return;
        // Um mergulho fundo (até quase apagar) lia como piscada; de 60 % o texto novo só assenta
        text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    /// <summary>O elemento acende de 60 % a 100 % em 180 ms (troca de texto ou de cor de um botão).</summary>
    public static void Soften(UIElement element)
    {
        if (!Animate || !element.IsVisible) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
    }

    // ---------------- Mensagens: a intermediária vira a final ----------------

    private sealed class Line
    {
        public long BusyAt;
        public bool Busy;
        public int Generation;
        public bool Swapping;
        public long SwapAt;
        public string Text = "", More = "";
        public System.Windows.Threading.DispatcherTimer? Timer;
    }

    private static readonly ConditionalWeakTable<TextBlock, Line> Lines = new();
    private static readonly TimeSpan MinBusy = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(130);
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(240);
    private static bool _quiet;

    /// <summary>
    /// Troca o estado (e o detalhe ao lado) com uma transição: o texto atual apaga, o novo entra subindo 4 px e acendendo.
    /// Uma mensagem intermediária ("Lendo drivers…", que termina em reticências) fica pelo menos 900 ms na tela: a final que chega
    /// antes disso espera a vez, para a leitura rápida não virar uma piscada de "Lendo…" seguida de "Lido às". Mensagens que chegam
    /// no meio de uma troca só atualizam o texto que vai entrar; a mais nova manda. O andamento de uma mesma etapa ("Baixando… 34 MiB",
    /// "Baixando… 41 MiB") troca só o número, direto, sem apagar e acender a cada passo, que piscava.
    /// </summary>
    public static void Say(TextBlock status, TextBlock? detail, string text, string more = "", bool? busy = null)
    {
        var line = Lines.GetValue(status, _ => new Line { Text = status.Text, More = detail?.Text ?? "" });
        bool isBusy = busy ?? text.Contains('…');
        line.Generation++;
        line.Timer?.Stop();
        line.Timer = null;
        long now = Environment.TickCount64;
        if (isBusy && line.Busy && !line.Swapping && status.Text == line.Text && Stem(line.Text) == Stem(text))
        {
            line.Text = text;
            line.More = more;
            Set(status, detail, text, more);
            return;
        }
        if (!isBusy && line.Busy && Animate && status.IsVisible)
        {
            var wait = MinBusy - TimeSpan.FromMilliseconds(now - line.BusyAt);
            if (wait > TimeSpan.FromMilliseconds(30))
            {
                int generation = line.Generation;
                var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, status.Dispatcher) { Interval = wait };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (generation != line.Generation) return;
                    line.Timer = null;
                    line.Busy = false;
                    Swap(status, detail, line, text, more);
                };
                line.Timer = timer;
                timer.Start();
                return;
            }
        }
        if (isBusy && (!line.Busy || line.Text != text)) line.BusyAt = now;
        line.Busy = isBusy;
        Swap(status, detail, line, text, more);
    }

    /// <summary>A etapa de uma mensagem de andamento: o texto até as reticências ("Copiando os drivers… 3 de 69" → "Copiando os drivers…").</summary>
    internal static string Stem(string text)
    {
        int cut = text.IndexOf('…');
        return cut < 0 ? text : text[..(cut + 1)];
    }

    private static void Set(TextBlock status, TextBlock? detail, string text, string more)
    {
        _quiet = true;
        try
        {
            status.Text = text;
            if (detail != null) detail.Text = more;
        }
        finally { _quiet = false; }
    }

    private static void Swap(TextBlock status, TextBlock? detail, Line line, string text, string more)
    {
        bool same = line.Text == text && line.More == more;
        line.Text = text;
        line.More = more;
        // A troca em andamento já vai pôr o texto mais novo (se outra animação a substituiu e ela não terminou, recomeça)
        if (line.Swapping && Environment.TickCount64 - line.SwapAt < 400) return;
        if (same && status.Text == text && (detail == null || detail.Text == more)) return;
        if (!Animate || !status.IsVisible || string.IsNullOrEmpty(status.Text))
        {
            Set(status, detail, text, more);
            if (Animate && status.IsVisible) Enter(status, detail);
            return;
        }
        line.Swapping = true;
        line.SwapAt = Environment.TickCount64;
        var fade = new DoubleAnimation(0, FadeOut) { EasingFunction = EaseInOut };
        fade.Completed += (_, _) =>
        {
            line.Swapping = false;
            Set(status, detail, line.Text, line.More);
            Enter(status, detail);
        };
        status.BeginAnimation(UIElement.OpacityProperty, fade);
        if (detail != null && !string.IsNullOrEmpty(detail.Text)) detail.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, FadeOut) { EasingFunction = EaseInOut });
    }

    private static void Enter(TextBlock status, TextBlock? detail)
    {
        foreach (var t in detail != null ? new[] { status, detail } : new[] { status })
        {
            if (t.RenderTransform is not TranslateTransform shift || shift.IsFrozen) t.RenderTransform = shift = new TranslateTransform();
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, FadeIn) { EasingFunction = EaseOut });
            t.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, FadeIn) { EasingFunction = EaseOut });
        }
    }

    // ---------------- Medidor de progresso ----------------

    private sealed class Meter
    {
        public required FrameworkElement Host;
        public required ScaleTransform Scale;
        public TextBlock? Percent;
        public double Width;
        public Thickness Margin;
        public bool Shown;
        public bool Hiding;
        public double Target;
        public int Generation;
        public EventHandler? Tick;
    }

    private static readonly ConditionalWeakTable<Border, Meter> Meters = new();

    private static Meter MeterOf(Border fill)
    {
        if (Meters.TryGetValue(fill, out var m)) return m;
        var track = (FrameworkElement)fill.Parent;
        // O medidor é o ancestral com o estilo TaskMeter; sem ele (barra solta num painel), o próprio trilho
        FrameworkElement host = track;
        var meterStyle = track.TryFindResource("TaskMeter") as Style;
        for (var e = track; e != null; e = VisualTreeHelper.GetParent(e) as FrameworkElement ?? e.Parent as FrameworkElement)
        {
            if (meterStyle != null && ReferenceEquals(e.Style, meterStyle)) { host = e; break; }
            if (e is UserControl) break;
        }
        m = new Meter
        {
            Host = host,
            Scale = (ScaleTransform)fill.RenderTransform,
            Percent = FindPercent(host),
            Width = host.Width,
            Margin = host.Margin,
        };
        Meters.AddOrUpdate(fill, m);
        return m;
    }

    private static TextBlock? FindPercent(FrameworkElement host)
    {
        if (host is not Panel panel) return null;
        foreach (var child in panel.Children)
            if (child is TextBlock t && ReferenceEquals(t.Style, host.TryFindResource("TaskPercent"))) return t;
        return null;
    }

    private static void ShowPercent(Meter m, double fraction)
    {
        if (m.Percent == null) return;
        m.Percent.Text = fraction > 0 ? Math.Round(fraction * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %" : "";
    }

    private static void StopTick(Meter m)
    {
        if (m.Tick == null) return;
        CompositionTarget.Rendering -= m.Tick;
        m.Tick = null;
    }

    /// <summary>
    /// Leva a barra até a fração nova. Na primeira fração o medidor abre e acende; ao chegar a 1 ele fica cheio por um instante
    /// e então apaga e fecha. Voltar para trás (uma tarefa nova) vai direto ao valor.
    /// </summary>
    public static void SetProgress(Border fill, double fraction)
    {
        var m = MeterOf(fill);
        fraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        // A mesma fração de novo (o motor repete a etapa enquanto troca o texto) não reinicia o deslize
        if (m.Shown && !m.Hiding && fraction == m.Target && fraction < 1) return;
        m.Target = fraction;
        m.Generation++;
        ShowMeter(m);
        StopTick(m);
        if (!Animate || fraction <= m.Scale.ScaleX)
        {
            m.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            m.Scale.ScaleX = fraction;
            ShowPercent(m, fraction);
        }
        else
        {
            // A porcentagem conta junto com a barra: lê a escala animada a cada quadro até a animação terminar
            m.Tick = (_, _) => ShowPercent(m, m.Scale.ScaleX);
            CompositionTarget.Rendering += m.Tick;
            var slide = new DoubleAnimation(fraction, Slide) { EasingFunction = EaseOut };
            slide.Completed += (_, _) => { StopTick(m); ShowPercent(m, fraction); };
            m.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, slide);
        }
        if (fraction >= 1) HideMeter(m, Animate ? Hold + (fraction > 0 ? Slide : TimeSpan.Zero) : TimeSpan.Zero);
    }

    /// <summary>A tarefa parou antes do fim (erro, cancelamento): o medidor apaga e fecha já, sem segurar.</summary>
    public static void EndProgress(Border fill)
    {
        var m = MeterOf(fill);
        m.Generation++;
        HideMeter(m, TimeSpan.Zero);
    }

    private static void ShowMeter(Meter m)
    {
        var host = m.Host;
        bool wasShown = m.Shown, wasHiding = m.Hiding;
        m.Shown = true;
        m.Hiding = false;
        // Já aberto ou abrindo: não mexe. Zerar as animações aqui fazia o medidor saltar para o fim da abertura (o "flick" das
        // etapas que chegam juntas, como 0, 20 e 40 % ao ler os drivers)
        if (wasShown && !wasHiding) return;
        host.Visibility = Visibility.Visible;
        if (wasHiding && Animate && !double.IsNaN(m.Width))
        {
            // Estava apagando: volta dali mesmo (sem From, cada animação parte do valor em que está)
            host.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, Open) { EasingFunction = EaseOut });
            host.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(m.Width, Open) { EasingFunction = EaseOut });
            host.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(m.Margin, Open) { EasingFunction = EaseOut });
            return;
        }
        host.BeginAnimation(UIElement.OpacityProperty, null);
        host.BeginAnimation(FrameworkElement.WidthProperty, null);
        host.BeginAnimation(FrameworkElement.MarginProperty, null);
        if (wasShown || !Animate || double.IsNaN(m.Width))
        {
            host.Opacity = 1;
            if (!double.IsNaN(m.Width)) host.Width = m.Width;
            host.Margin = m.Margin;
            return;
        }
        // Abre a largura (o estado à esquerda desliza, os botões ficam onde estão) e acende um pouco depois
        host.Width = m.Width;
        host.Margin = m.Margin;
        host.Opacity = 0;
        host.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(0, m.Width, Open) { EasingFunction = EaseOut });
        host.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(new Thickness(0), m.Margin, Open) { EasingFunction = EaseOut });
        host.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Open) { BeginTime = TimeSpan.FromMilliseconds(80), EasingFunction = EaseOut });
    }

    private static void HideMeter(Meter m, TimeSpan delay)
    {
        var host = m.Host;
        int generation = m.Generation;
        void Collapse()
        {
            if (generation != m.Generation) return; // uma tarefa nova começou no meio: ela manda
            StopTick(m);
            m.Shown = false;
            m.Hiding = false;
            m.Target = 0;
            host.BeginAnimation(UIElement.OpacityProperty, null);
            host.BeginAnimation(FrameworkElement.WidthProperty, null);
            host.BeginAnimation(FrameworkElement.MarginProperty, null);
            host.Visibility = Visibility.Collapsed;
            host.Opacity = 1;
            if (!double.IsNaN(m.Width)) host.Width = m.Width;
            host.Margin = m.Margin;
            m.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            m.Scale.ScaleX = 0;
            ShowPercent(m, 0);
        }
        if (!m.Shown || !Animate || host.Visibility != Visibility.Visible) { Collapse(); return; }
        m.Hiding = true;

        // Apaga primeiro e fecha a largura logo depois, para o estado voltar ao lugar devagar
        var fade = new DoubleAnimation(0, Close) { BeginTime = delay, EasingFunction = EaseInOut };
        host.BeginAnimation(UIElement.OpacityProperty, fade);
        if (!double.IsNaN(m.Width))
        {
            var shrink = new DoubleAnimation(0, Close) { BeginTime = delay + TimeSpan.FromMilliseconds(120), EasingFunction = EaseInOut };
            shrink.Completed += (_, _) => Collapse();
            host.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(new Thickness(0), Close) { BeginTime = delay + TimeSpan.FromMilliseconds(120), EasingFunction = EaseInOut });
            host.BeginAnimation(FrameworkElement.WidthProperty, shrink);
        }
        else fade.Completed += (_, _) => Collapse();
    }
}
