using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop;

/// <summary>
/// Propriedades anexadas do design system: ícone (glifo Segoe Fluent), legenda, texto de espera, sufixo de unidade, canal
/// do componente e a luz de um botão sob o mouse. Os templates de Theme.xaml leem estes valores; as páginas só os declaram.
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string value) => d.SetValue(IconProperty, value);

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.RegisterAttached(
        "Caption", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetCaption(DependencyObject d) => (string)d.GetValue(CaptionProperty);
    public static void SetCaption(DependencyObject d, string value) => d.SetValue(CaptionProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.RegisterAttached(
        "Suffix", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetSuffix(DependencyObject d) => (string)d.GetValue(SuffixProperty);
    public static void SetSuffix(DependencyObject d, string value) => d.SetValue(SuffixProperty, value);

    /// <summary>
    /// Componente de hardware (Cpu, Gpu, Memory, Virtual, Storage, Board, Net ou Signal; vazio = Steel): o ícone (ChannelIcon) e o
    /// fundo claro da ficha (ChannelWash) pintam por ele, para cada peça ter a mesma cor em todas as telas.
    /// </summary>
    public static readonly DependencyProperty ChannelProperty = DependencyProperty.RegisterAttached(
        "Channel", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetChannel(DependencyObject d) => (string)d.GetValue(ChannelProperty);
    public static void SetChannel(DependencyObject d, string value) => d.SetValue(ChannelProperty, value);

    /// <summary>Cor própria do glifo de um botão (vazio = a cor do texto): os botões fantasmas acendem o glifo no sinal.</summary>
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.RegisterAttached(
        "IconBrush", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetIconBrush(DependencyObject d) => (Brush?)d.GetValue(IconBrushProperty);
    public static void SetIconBrush(DependencyObject d, Brush? value) => d.SetValue(IconBrushProperty, value);

    /// <summary>Medidor (ScaleTransform de 0 a 1) que desliza até a leitura nova em vez de saltar; sem animate, vai direto.</summary>
    public static void Glide(ScaleTransform meter, double to, bool animate)
    {
        to = Math.Clamp(double.IsFinite(to) ? to : 0, 0, 1);
        if (!animate)
        {
            meter.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            meter.ScaleX = to;
            return;
        }
        meter.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Camada que acende por cima de um botão quando o mouse passa (cada estilo de botão escolhe a sua).</summary>
    public static readonly DependencyProperty HoverFillProperty = DependencyProperty.RegisterAttached(
        "HoverFill", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverFill(DependencyObject d) => (Brush?)d.GetValue(HoverFillProperty);
    public static void SetHoverFill(DependencyObject d, Brush? value) => d.SetValue(HoverFillProperty, value);

    /// <summary>
    /// Verdadeiro dentro de um SlidingSelectionPanel (os filhos herdam): o item marcado não pinta o próprio fundo, porque o
    /// painel desenha um destaque só e o desliza até ele.
    /// </summary>
    public static readonly DependencyProperty SlidingGroupProperty = DependencyProperty.RegisterAttached(
        "SlidingGroup", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetSlidingGroup(DependencyObject d) => (bool)d.GetValue(SlidingGroupProperty);
    public static void SetSlidingGroup(DependencyObject d, bool value) => d.SetValue(SlidingGroupProperty, value);

    /// <summary>
    /// Botão de iniciar/parar: o mesmo lugar troca de papel (ação principal ↔ interromper),
    /// com cor, glifo e texto coerentes. Assim nenhum teste depende só do texto para mostrar que está rodando.
    /// </summary>
    public static void SetRunning(System.Windows.Controls.Button button, bool running, string idleText, string runningText = "Parar", string idleIcon = "\uE768")
    {
        if (button.TryFindResource(running ? "StopButton" : "PrimaryButton") is Style style) button.Style = style;
        SetIcon(button, running ? "\uE71A" : idleIcon);
        button.Content = running ? runningText : idleText;
    }

    /// <summary>
    /// Selo de veredito (estilo Badge com um LED e um texto dentro): tone \u00E9 "Ok", "Warn", "Danger" ou "" (sem dados).
    /// Saud\u00E1vel fica em tinta com o LED verde; aten\u00E7\u00E3o e cr\u00EDtico ganham fundo e texto na cor, porque pedem que algu\u00E9m olhe.
    /// </summary>
    public static void SetTone(System.Windows.Controls.Border badge, string tone)
    {
        bool alert = tone is "Warn" or "Danger";
        string soft = tone + "Soft";
        if (alert)
        {
            badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, soft);
            badge.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, soft);
        }
        else
        {
            badge.ClearValue(System.Windows.Controls.Border.BackgroundProperty); // o estilo Badge é transparente com fio
            badge.ClearValue(System.Windows.Controls.Border.BorderBrushProperty);
        }
        if (badge.Child is not System.Windows.Controls.Panel panel) return;
        foreach (var child in panel.Children)
        {
            if (child is System.Windows.Shapes.Ellipse led)
                led.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, tone.Length > 0 ? tone : "Faint");
            else if (child is System.Windows.Controls.TextBlock text)
                text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, alert ? tone : tone == "Ok" ? "Fg" : "Muted");
        }
    }

    /// <summary>Raio dos pain\u00E9is (RadiusPanel), para recortar cantos desenhados em c\u00F3digo.</summary>
    public static double PanelRadius(FrameworkElement element) =>
        element.TryFindResource("RadiusPanel") is CornerRadius r ? r.TopLeft : 10;
}

/// <summary>Texto vazio some do layout (rótulos opcionais em templates).</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object value, Type target, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Rótulos do motor chegam em caixa alta ("SAUDÁVEL", "INJOGÁVEL") porque o relatório usa esse formato;
/// na interface os selos ficam em caixa de frase ("Saudável").
/// </summary>
public sealed class SentenceCaseConverter : IValueConverter
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public object Convert(object value, Type target, object parameter, CultureInfo culture)
    {
        if (value is not string s || s.Length == 0) return value ?? "";
        string lower = s.ToLower(PtBr);
        return char.ToUpper(lower[0], PtBr) + lower[1..];
    }
    public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Ocupa o espaço que a página der sem pedir altura própria (além do MinHeight). Serve para um Viewbox numa linha "*":
/// sozinho, ele pediria a altura da largura toda e faria a página rolar com a janela maximizada.
/// </summary>
public sealed class FillBox : System.Windows.Controls.Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        // Mede o filho no mínimo; no arranjo um Viewbox cresce sozinho até o espaço que a página der.
        Child?.Measure(new Size(constraint.Width, Math.Min(constraint.Height, MinHeight)));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}

/// <summary>
/// Pilha vertical que reparte a sobra de altura igualmente entre os filhos (cada um cresce e centraliza o conteúdo),
/// em vez de deixar um vão vazio no fim do painel. Sem sobra, comporta-se como um StackPanel.
/// </summary>
public sealed class SpreadPanel : System.Windows.Controls.Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double w = 0, h = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            w = Math.Max(w, child.DesiredSize.Width);
            h += child.DesiredSize.Height;
        }
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double used = visible.Sum(c => c.DesiredSize.Height);
        double extra = visible.Count > 0 ? Math.Max(0, final.Height - used) / visible.Count : 0;
        double y = 0;
        foreach (var child in visible)
        {
            double h = child.DesiredSize.Height + extra;
            child.Arrange(new Rect(0, y, final.Width, h));
            y += h;
        }
        return final;
    }
}

/// <summary>
/// Linhas de itens que dividem a sobra de largura em vãos iguais, inclusive o vão antes do primeiro item (o último encosta na direita);
/// quando não cabem, quebram em mais linhas, cada uma distribuída do mesmo jeito.
/// </summary>
public sealed class JustifyPanel : System.Windows.Controls.Panel
{
    private List<List<UIElement>> Lines(double width)
    {
        var lines = new List<List<UIElement>> { new() };
        double x = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            double w = c.DesiredSize.Width;
            if (lines[^1].Count > 0 && x + w > width) { lines.Add(new()); x = 0; }
            lines[^1].Add(c);
            x += w;
        }
        return lines;
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement c in InternalChildren) c.Measure(new Size(double.PositiveInfinity, available.Height));
        var lines = Lines(available.Width);
        double h = lines.Sum(l => l.Count == 0 ? 0 : l.Max(c => c.DesiredSize.Height));
        double w = lines.Max(l => l.Sum(c => c.DesiredSize.Width));
        return new Size(double.IsInfinity(available.Width) ? w : Math.Min(w, available.Width), h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        foreach (var line in Lines(final.Width))
        {
            if (line.Count == 0) continue;
            double lineH = line.Max(c => c.DesiredSize.Height);
            double gap = line.Count > 1 ? Math.Max(0, (final.Width - line.Sum(c => c.DesiredSize.Width)) / line.Count) : 0;
            double x = gap;
            foreach (var c in line)
            {
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, lineH));
                x += c.DesiredSize.Width + gap;
            }
            y += lineH;
        }
        return final;
    }
}

/// <summary>
/// Pilha de opções (RadioButton) com um destaque só, que desliza até a marcada em vez de saltar de uma para a outra: a
/// navegação lateral e os controles segmentados. Os itens sabem que estão aqui por Ui.SlidingGroup e não pintam o fundo.
/// Quando só o layout muda (janela redimensionada), o destaque acompanha sem animar.
/// </summary>
public sealed class SlidingSelectionPanel : System.Windows.Controls.StackPanel
{
    private static readonly Duration SlideTime = TimeSpan.FromMilliseconds(260);
    private static readonly Duration FadeTime = TimeSpan.FromMilliseconds(140);

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(Brush), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Fundo do destaque.</summary>
    public Brush? Highlight { get => (Brush?)GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }

    public static readonly DependencyProperty HighlightBorderProperty = DependencyProperty.Register(
        nameof(HighlightBorder), typeof(Brush), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Fio de 1 px em volta do destaque (opcional).</summary>
    public Brush? HighlightBorder { get => (Brush?)GetValue(HighlightBorderProperty); set => SetValue(HighlightBorderProperty, value); }

    public static readonly DependencyProperty HighlightRadiusProperty = DependencyProperty.Register(
        nameof(HighlightRadius), typeof(double), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(7.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double HighlightRadius { get => (double)GetValue(HighlightRadiusProperty); set => SetValue(HighlightRadiusProperty, value); }

    // Onde o destaque está agora (animado) e o quanto aparece
    private static readonly DependencyProperty SpotProperty = DependencyProperty.Register(
        "Spot", typeof(Rect), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty SpotOpacityProperty = DependencyProperty.Register(
        "SpotOpacity", typeof(double), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private Rect _target = Rect.Empty;
    private bool _shown;

    public SlidingSelectionPanel()
    {
        SetValue(Ui.SlidingGroupProperty, true);
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Follow(animate: true)));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => Follow(animate: true)));
        IsEnabledChanged += (_, _) => InvalidateVisual(); // desligado, o destaque esmaece junto com as opções
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);
        Follow(animate: false);
        return size;
    }

    /// <summary>Leva o destaque até a opção marcada: deslizando quando a escolha muda, direto quando só o layout mudou.</summary>
    private void Follow(bool animate)
    {
        var item = Children.OfType<ToggleButton>().FirstOrDefault(c => c.IsChecked == true && c.Visibility == Visibility.Visible);
        if (item is null || item.RenderSize.Width <= 0)
        {
            Show(false, animate);
            return;
        }
        var target = new Rect(item.TranslatePoint(new Point(), this), item.RenderSize);
        if (target != _target)
        {
            bool slide = animate && _shown && !_target.IsEmpty;
            _target = target;
            if (slide)
            {
                BeginAnimation(SpotProperty, new RectAnimation(target, SlideTime) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                BeginAnimation(SpotProperty, null);
                SetValue(SpotProperty, target);
            }
        }
        Show(true, animate);
    }

    private void Show(bool visible, bool animate)
    {
        if (visible == _shown) return;
        _shown = visible;
        double to = visible ? 1 : 0;
        if (animate)
        {
            BeginAnimation(SpotOpacityProperty, new DoubleAnimation(to, FadeTime));
        }
        else
        {
            BeginAnimation(SpotOpacityProperty, null);
            SetValue(SpotOpacityProperty, to);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var spot = (Rect)GetValue(SpotProperty);
        double opacity = (double)GetValue(SpotOpacityProperty) * (IsEnabled ? 1 : 0.45);
        if (spot.IsEmpty || opacity <= 0) return;
        Pen? pen = HighlightBorder is { } line ? new Pen(line, 1) : null;
        if (pen is not null) spot.Inflate(-0.5, -0.5); // o fio cai inteiro dentro do pixel
        if (spot.Width <= 0 || spot.Height <= 0) return;
        dc.PushOpacity(opacity);
        dc.DrawRoundedRectangle(Highlight, pen, spot, HighlightRadius, HighlightRadius);
        dc.Pop();
    }
}
