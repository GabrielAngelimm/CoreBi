using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop;

/// <summary>
/// Controle deslizante desenhado à mão (estilo <c>ConfigSlider</c> de Theme.xaml): trilho em pílula, a parte até o botão acesa
/// na cor do ajuste e o botão redondo recortado do cartão por um anel. Anda de passo em passo (Step): clicar no trilho leva o
/// botão até ali deslizando, arrastar acompanha o mouse, as setas do teclado e a roda do mouse andam um passo, Page Up/Down
/// andam LargeChange e Home/End vão às pontas. Com poucos passos (até 12), cada um ganha um ponto no trilho. Sob o mouse, no
/// arrasto e com o foco do teclado, um halo na cor do ajuste cresce em volta do botão. Desligado, não reage e perde o halo; quem
/// esmaece é a seção em volta (ConfigSection), junto com o rótulo e o valor.
/// </summary>
public sealed class ValueSlider : RangeBase
{
    private const double TrackHeight = 6;
    private const double ThumbRadius = 9;
    private const double RingWidth = 3;
    private const int MaxDots = 12;
    private static readonly Duration GlideTime = TimeSpan.FromMilliseconds(180);
    private static readonly Duration HaloTime = TimeSpan.FromMilliseconds(140);

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(ValueSlider), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>O passo: o valor sempre cai num múltiplo dele a partir do mínimo.</summary>
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(ValueSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ValueSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>A cor do ajuste: a parte acesa do trilho, o botão e o halo.</summary>
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public static readonly DependencyProperty RingProperty = DependencyProperty.Register(
        nameof(Ring), typeof(Brush), typeof(ValueSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>O anel do botão e os pontos sobre a parte acesa: a cor do cartão, para o botão parecer recortado.</summary>
    public Brush? Ring { get => (Brush?)GetValue(RingProperty); set => SetValue(RingProperty, value); }

    public static readonly DependencyProperty DotProperty = DependencyProperty.Register(
        nameof(Dot), typeof(Brush), typeof(ValueSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Os pontos dos passos sobre a parte apagada do trilho.</summary>
    public Brush? Dot { get => (Brush?)GetValue(DotProperty); set => SetValue(DotProperty, value); }

    // Onde o botão está desenhado (anima até Value) e o tamanho do halo (0 sem halo, 1 sob o mouse, 1.25 no arrasto)
    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(ValueSlider), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty HaloProperty = DependencyProperty.Register(
        "Halo", typeof(double), typeof(ValueSlider), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _dragging;
    private bool _glide;

    static ValueSlider()
    {
        FocusableProperty.OverrideMetadata(typeof(ValueSlider), new FrameworkPropertyMetadata(true));
        IsEnabledProperty.OverrideMetadata(typeof(ValueSlider), new UIPropertyMetadata((d, _) => ((ValueSlider)d).InvalidateVisual()));
    }

    public ValueSlider()
    {
        Cursor = Cursors.Hand;
        SetValue(ShownProperty, Value);
    }

    protected override Size MeasureOverride(Size constraint) =>
        new(double.IsInfinity(constraint.Width) ? 160 : constraint.Width, double.IsNaN(Height) ? 24 : Height);

    protected override Size ArrangeOverride(Size arrangeBounds) => arrangeBounds;

    /// <summary>O valor no passo mais próximo, dentro da faixa.</summary>
    private double Snap(double v)
    {
        double step = Step > 0 ? Step : 1;
        v = Math.Clamp(v, Minimum, Maximum);
        return Math.Clamp(Minimum + Math.Round((v - Minimum) / step) * step, Minimum, Maximum);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        double snapped = Snap(newValue);
        if (snapped != newValue) { Value = snapped; return; }
        if (_glide && !_dragging && SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(ShownProperty, new DoubleAnimation(newValue, GlideTime) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            BeginAnimation(ShownProperty, null);
            SetValue(ShownProperty, newValue);
        }
    }

    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        InvalidateVisual();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        InvalidateVisual();
    }

    /// <summary>Troca o valor pela interação: desliza até lá (fora do arrasto).</summary>
    private void MoveTo(double v)
    {
        _glide = true;
        try { Value = Snap(v); }
        finally { _glide = false; }
    }

    // Trilho: de ponta a ponta menos o raio do botão de cada lado, para o botão nunca sair do controle
    private double Left => ThumbRadius + 2;
    private double Usable => Math.Max(1, ActualWidth - 2 * Left);
    private double XOf(double v) => Left + (Maximum > Minimum ? (v - Minimum) / (Maximum - Minimum) : 0) * Usable;
    private double ValueAt(double x) => Minimum + Math.Clamp((x - Left) / Usable, 0, 1) * (Maximum - Minimum);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (!CaptureMouse()) return;
        SetHalo(1.25);
        // O clique no trilho leva o botão até ali deslizando; o arrasto que segue acompanha o mouse
        MoveTo(ValueAt(e.GetPosition(this).X));
        _dragging = true;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        Value = Snap(ValueAt(e.GetPosition(this).X));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging) ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
        SetHalo(IsMouseOver || IsKeyboardFocused ? 1 : 0);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (!_dragging) SetHalo(1);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging) SetHalo(IsKeyboardFocused && InputManager.Current.MostRecentInputDevice is KeyboardDevice ? 1 : 0);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) SetHalo(1);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (!_dragging && !IsMouseOver) SetHalo(0);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        MoveTo(Value + Math.Sign(e.Delta) * StepSize(SmallChange));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double? to = e.Key switch
        {
            Key.Left or Key.Down => Value - StepSize(SmallChange),
            Key.Right or Key.Up => Value + StepSize(SmallChange),
            Key.PageDown => Value - StepSize(LargeChange),
            Key.PageUp => Value + StepSize(LargeChange),
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (to is double v)
        {
            MoveTo(v);
            SetHalo(1);
            e.Handled = true;
        }
    }

    private double StepSize(double change) => Math.Max(change, Step > 0 ? Step : 1);

    private void SetHalo(double to)
    {
        if (!SystemParameters.ClientAreaAnimation) { BeginAnimation(HaloProperty, null); SetValue(HaloProperty, to); return; }
        BeginAnimation(HaloProperty, new DoubleAnimation(to, HaloTime) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Fundo transparente na área toda: o clique em qualquer ponto da faixa conta, não só no trilho
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ActualWidth <= 2 * Left) return;
        double cy = Math.Round(ActualHeight / 2);
        double x = XOf(Math.Clamp((double)GetValue(ShownProperty), Minimum, Maximum));
        double halo = (double)GetValue(HaloProperty);
        // Desligado, nada de cor e a 30 %, como as pílulas: a parte acesa e o botão ficam no tom dos pontos, para não parecer
        // que dá para mexer
        var fill = IsEnabled ? Fill : Dot ?? Fill;
        if (!IsEnabled) dc.PushOpacity(0.3);

        var track = new Rect(Left - TrackHeight / 2, cy - TrackHeight / 2, Usable + TrackHeight, TrackHeight);
        dc.DrawRoundedRectangle(Track, null, track, TrackHeight / 2, TrackHeight / 2);
        var lit = new Rect(track.Left, track.Top, Math.Max(TrackHeight, x - track.Left), TrackHeight);
        dc.DrawRoundedRectangle(fill, null, lit, TrackHeight / 2, TrackHeight / 2);

        // Um ponto por passo quando são poucos (a duração): o trilho mostra onde o botão pode parar
        double step = Step > 0 ? Step : 1;
        int count = (int)Math.Round((Maximum - Minimum) / step);
        if (count is > 1 and <= MaxDots)
        {
            for (int i = 1; i < count; i++)
            {
                double dx = XOf(Minimum + i * step);
                if (Math.Abs(dx - x) < ThumbRadius + 2) continue;
                dc.DrawEllipse(dx < x ? Ring : Dot, null, new Point(dx, cy), 1.5, 1.5);
            }
        }

        if (halo > 0 && IsEnabled && Fill is SolidColorBrush solid)
        {
            var c = solid.Color;
            var soft = new SolidColorBrush(Color.FromArgb((byte)(c.A * 0.18), c.R, c.G, c.B));
            soft.Freeze();
            dc.DrawEllipse(soft, null, new Point(x, cy), ThumbRadius + 6 * halo, ThumbRadius + 6 * halo);
        }
        var ring = Ring is { } r ? new Pen(r, RingWidth) : null;
        ring?.Freeze();
        dc.DrawEllipse(fill, ring, new Point(x, cy), ThumbRadius - RingWidth / 2, ThumbRadius - RingWidth / 2);
        if (!IsEnabled) dc.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ValueSlider owner) : RangeBaseAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(ValueSlider);
    }
}
