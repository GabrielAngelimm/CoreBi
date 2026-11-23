using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop;

/// <summary>Contrato de janela que informa se animações ao vivo estão liberadas (janela ativa e sem benchmark/stress).</summary>
public interface ILiveMotionHost
{
    bool LiveMotion { get; }
}

/// <summary>
/// Base compartilhada de todos os instrumentos desenhados no DrawingContext (Discos, Energia, GPU e Benchmarks).
/// Os pincéis seguem a paleta via SetResourceReference e acompanham a troca de tema automaticamente.
/// </summary>
public abstract class InstrumentBase : FrameworkElement
{
    private static DependencyProperty Paint(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(InstrumentBase),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChannelBrushProperty = Paint(nameof(ChannelBrush));
    public static readonly DependencyProperty TrackBrushProperty = Paint(nameof(TrackBrush));
    public static readonly DependencyProperty InkBrushProperty = Paint(nameof(InkBrush));
    public static readonly DependencyProperty MutedBrushProperty = Paint(nameof(MutedBrush));
    public static readonly DependencyProperty FaintBrushProperty = Paint(nameof(FaintBrush));
    public static readonly DependencyProperty SurfaceBrushProperty = Paint(nameof(SurfaceBrush));
    public static readonly DependencyProperty WarnBrushProperty = Paint(nameof(WarnBrush));
    public static readonly DependencyProperty WarnSoftBrushProperty = Paint(nameof(WarnSoftBrush));
    public static readonly DependencyProperty DangerBrushProperty = Paint(nameof(DangerBrush));
    public static readonly DependencyProperty DangerSoftBrushProperty = Paint(nameof(DangerSoftBrush));
    public static readonly DependencyProperty RaisedBrushProperty = Paint(nameof(RaisedBrush));
    public static readonly DependencyProperty LineStrongBrushProperty = Paint(nameof(LineStrongBrush));
    public static readonly DependencyProperty CpuBrushProperty = Paint(nameof(CpuBrush));
    public static readonly DependencyProperty GpuBrushProperty = Paint(nameof(GpuBrush));
    public static readonly DependencyProperty SteelBrushProperty = Paint(nameof(SteelBrush));
    public static readonly DependencyProperty SignalBrushProperty = Paint(nameof(SignalBrush));

    public Brush ChannelBrush { get => (Brush)GetValue(ChannelBrushProperty); set => SetValue(ChannelBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush InkBrush { get => (Brush)GetValue(InkBrushProperty); set => SetValue(InkBrushProperty, value); }
    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush FaintBrush { get => (Brush)GetValue(FaintBrushProperty); set => SetValue(FaintBrushProperty, value); }
    public Brush SurfaceBrush { get => (Brush)GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }
    public Brush WarnBrush { get => (Brush)GetValue(WarnBrushProperty); set => SetValue(WarnBrushProperty, value); }
    public Brush WarnSoftBrush { get => (Brush)GetValue(WarnSoftBrushProperty); set => SetValue(WarnSoftBrushProperty, value); }
    public Brush DangerBrush { get => (Brush)GetValue(DangerBrushProperty); set => SetValue(DangerBrushProperty, value); }
    public Brush DangerSoftBrush { get => (Brush)GetValue(DangerSoftBrushProperty); set => SetValue(DangerSoftBrushProperty, value); }
    public Brush RaisedBrush { get => (Brush)GetValue(RaisedBrushProperty); set => SetValue(RaisedBrushProperty, value); }
    public Brush LineStrongBrush { get => (Brush)GetValue(LineStrongBrushProperty); set => SetValue(LineStrongBrushProperty, value); }
    public Brush CpuBrush { get => (Brush)GetValue(CpuBrushProperty); set => SetValue(CpuBrushProperty, value); }
    public Brush GpuBrush { get => (Brush)GetValue(GpuBrushProperty); set => SetValue(GpuBrushProperty, value); }
    public Brush SteelBrush { get => (Brush)GetValue(SteelBrushProperty); set => SetValue(SteelBrushProperty, value); }
    public Brush SignalBrush { get => (Brush)GetValue(SignalBrushProperty); set => SetValue(SignalBrushProperty, value); }

    protected const double SegmentPeriod = 5, SegmentGap = 1.5;

    protected InstrumentBase()
    {
        SnapsToDevicePixels = true;
        SetResourceReference(TrackBrushProperty, "Line");
        SetResourceReference(InkBrushProperty, "Fg");
        SetResourceReference(MutedBrushProperty, "Muted");
        SetResourceReference(FaintBrushProperty, "Faint");
        SetResourceReference(SurfaceBrushProperty, "Surface");
        SetResourceReference(WarnBrushProperty, "Warn");
        SetResourceReference(WarnSoftBrushProperty, "WarnSoft");
        SetResourceReference(DangerBrushProperty, "Danger");
        SetResourceReference(DangerSoftBrushProperty, "DangerSoft");
        SetResourceReference(RaisedBrushProperty, "Raised");
        SetResourceReference(LineStrongBrushProperty, "LineStrong");
        SetResourceReference(CpuBrushProperty, "Cpu");
        SetResourceReference(GpuBrushProperty, "Gpu");
        SetResourceReference(SteelBrushProperty, "Steel");
        SetResourceReference(SignalBrushProperty, "Signal");
    }

    protected static DependencyProperty Number(string name, Type owner, double initial = double.NaN) =>
        DependencyProperty.Register(name, typeof(double), owner,
            new FrameworkPropertyMetadata(initial, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Eixos, marcas e dicas dos gráficos: o piso da escala (nada abaixo de 12), igual à Visão geral e ao Teste de carga.</summary>
    protected const double AxisText = 12;

    /// <summary>Raio da caixa de dica: o RadiusControl, o mesmo das dicas da Visão geral e do Teste de carga.</summary>
    protected double TipRadius => TryFindResource("RadiusControl") is CornerRadius r ? r.TopLeft : 12;

    protected FormattedText Text(string text, double size, Brush brush, bool readout = true, FontWeight? weight = null)
    {
        var family = (TryFindResource(readout ? "Readout" : "UiFont") as FontFamily) ?? new FontFamily("Segoe UI");
        var face = new Typeface(family, FontStyles.Normal, weight ?? FontWeights.Normal, readout ? FontStretches.SemiCondensed : FontStretches.Normal);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected double XHeight(double size, bool readout = true)
    {
        var family = (TryFindResource(readout ? "Readout" : "UiFont") as FontFamily) ?? new FontFamily("Segoe UI");
        return new Typeface(family, FontStyles.Normal, FontWeights.Normal, readout ? FontStretches.SemiCondensed : FontStretches.Normal).XHeight * size;
    }

    protected static Pen Stroke(Brush brush, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(brush, thickness) { DashStyle = dash ?? DashStyles.Solid };
        pen.Freeze();
        return pen;
    }

    protected static Color ColorOf(Brush brush, Color fallback) => brush is SolidColorBrush s ? s.Color : fallback;

    protected static Brush Alpha(Brush brush, byte alpha)
    {
        var c = ColorOf(brush, Colors.Gray);
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    protected static void Segments(DrawingContext dc, Rect area, Func<double, Brush> color)
    {
        int n = (int)Math.Floor((area.Width + SegmentGap) / SegmentPeriod);
        for (int i = 0; i < n; i++)
            dc.DrawRectangle(color((i + 0.5) / n), null, new Rect(area.X + i * SegmentPeriod, area.Y, SegmentPeriod - SegmentGap, area.Height));
    }

    protected static StreamGeometry CreateAreaGeometry(IList<Point> points, double floorY)
    {
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, floorY), true, true);
            g.PolyLineTo(points, true, false);
            g.LineTo(new Point(points[^1].X, floorY), false, false);
        }
        area.Freeze();
        return area;
    }

    protected static StreamGeometry CreatePolylineGeometry(IList<Point> points)
    {
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false, false);
            g.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        line.Freeze();
        return line;
    }

    protected void Pulse(DrawingContext dc, Color color, Point at)
    {
        if (Window.GetWindow(this) is not ILiveMotionHost { LiveMotion: true }) return;
        var halo = new SolidColorBrush(Color.FromArgb(0x50, color.R, color.G, color.B));
        halo.Freeze();
        var span = TimeSpan.FromMilliseconds(400);
        var grow = new DoubleAnimation(5, 12, span) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }.CreateClock();
        var fade = new DoubleAnimation(1, 0, span).CreateClock();
        dc.PushOpacity(1, fade);
        dc.DrawEllipse(halo, null, at, null, 5, grow, 5, grow);
        dc.Pop();
    }
}
