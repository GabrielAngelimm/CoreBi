using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop;

/// <summary>
/// Movimento da barra de ações do cabeçalho, sem nenhuma animação contínua:
/// <list type="bullet">
/// <item>o estado (<see cref="SoftTextProperty"/>) acende em 200 ms quando o texto muda, em vez de trocar seco;</item>
/// <item>o medidor de progresso (TaskMeter) abre a largura e acende ao começar (260 ms), a barra desliza até cada fração nova
/// e a porcentagem conta junto com ela, quadro a quadro; ao chegar a 100 % ele fica cheio por um instante e então apaga e
/// fecha a largura (300 ms), sem empurrar os botões, que ficam rentes à direita;</item>
/// <item>um botão que troca de texto ou de cor (<see cref="Soften"/>) acende em 180 ms.</item>
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
        if (sender is not TextBlock text || string.IsNullOrEmpty(text.Text) || !Animate || !text.IsVisible) return;
        // Texto que muda várias vezes por segundo (o andamento do teste de rede) não acende a cada troca: piscaria
        long now = Environment.TickCount64;
        long last = (long)text.GetValue(SoftAtProperty);
        text.SetValue(SoftAtProperty, now);
        if (now - last < 700) return;
        text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = EaseOut });
    }

    /// <summary>O elemento acende de 55 % a 100 % em 180 ms (troca de texto ou de cor de um botão).</summary>
    public static void Soften(UIElement element)
    {
        if (!Animate || !element.IsVisible) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = EaseOut });
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
        bool wasShown = m.Shown;
        m.Shown = true;
        host.BeginAnimation(UIElement.OpacityProperty, null);
        host.BeginAnimation(FrameworkElement.WidthProperty, null);
        host.BeginAnimation(FrameworkElement.MarginProperty, null);
        host.Visibility = Visibility.Visible;
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
