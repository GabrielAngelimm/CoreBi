using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop;

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
        nameof(HighlightRadius), typeof(double), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double HighlightRadius { get => (double)GetValue(HighlightRadiusProperty); set => SetValue(HighlightRadiusProperty, value); }

    // Onde o destaque está agora (animado) e o quanto aparece
    private static readonly DependencyProperty SpotProperty = DependencyProperty.Register(
        "Spot", typeof(Rect), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty SpotOpacityProperty = DependencyProperty.Register(
        "SpotOpacity", typeof(double), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UniformProperty = DependencyProperty.Register(
        nameof(Uniform), typeof(bool), typeof(SlidingSelectionPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));
    /// <summary>
    /// Opções em colunas iguais que dividem a largura toda (os controles da configuração dos testes): o texto fica no meio de
    /// cada tecla e nenhuma fica mais estreita que a maior.
    /// </summary>
    public bool Uniform { get => (bool)GetValue(UniformProperty); set => SetValue(UniformProperty, value); }

    private Rect _target = Rect.Empty;
    private bool _shown;

    public SlidingSelectionPanel()
    {
        SetValue(Ui.SlidingGroupProperty, true);
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Follow(animate: true)));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => Follow(animate: true)));
        IsEnabledChanged += (_, _) => InvalidateVisual(); // desligado, o destaque esmaece junto com as opções
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!Uniform || Orientation != System.Windows.Controls.Orientation.Horizontal) return base.MeasureOverride(constraint);
        var items = Children.OfType<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double widest = 0, tallest = 0;
        foreach (var c in items)
        {
            c.Measure(new Size(double.PositiveInfinity, constraint.Height));
            widest = Math.Max(widest, c.DesiredSize.Width);
            tallest = Math.Max(tallest, c.DesiredSize.Height);
        }
        // Pede a largura de todas iguais à maior, mas nunca mais que o espaço dado: o arranjo divide o que houver
        double width = widest * items.Count;
        if (!double.IsInfinity(constraint.Width)) width = Math.Min(width, constraint.Width);
        return new Size(width, tallest);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Size size;
        if (Uniform && Orientation == System.Windows.Controls.Orientation.Horizontal)
        {
            var items = Children.OfType<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
            double each = items.Count > 0 ? arrangeSize.Width / items.Count : 0;
            // Colunas iguais quando todas cabem; se uma não cabe ("Tela cheia" entre "720p" e "1440p"), cada uma fica com a
            // largura dela mais a mesma parte da sobra, e nenhuma corta o texto
            double natural = items.Sum(c => c.DesiredSize.Width);
            bool fits = items.All(c => c.DesiredSize.Width <= each + 0.5);
            double extra = items.Count > 0 ? Math.Max(0, arrangeSize.Width - natural) / items.Count : 0;
            double x = 0;
            foreach (var c in items)
            {
                double width = fits ? each : c.DesiredSize.Width + extra;
                c.Arrange(new Rect(x, 0, width, arrangeSize.Height));
                x += width;
            }
            size = arrangeSize;
        }
        else size = base.ArrangeOverride(arrangeSize);
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
        pen?.Freeze();
        if (pen is not null) spot.Inflate(-0.5, -0.5); // o fio cai inteiro dentro do pixel
        if (spot.Width <= 0 || spot.Height <= 0) return;
        dc.PushOpacity(opacity);
        dc.DrawRoundedRectangle(Highlight, pen, spot, HighlightRadius, HighlightRadius);
        dc.Pop();
    }
}
