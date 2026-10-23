using CoreBi.Engine.Monitoring.Storage;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Base dos instrumentos da página Discos, desenhados direto no DrawingContext. Os pincéis seguem a paleta por
/// referência de recurso: a troca de tema muda a propriedade e o instrumento se redesenha sozinho.
/// </summary>
public abstract class DiskInstrument : FrameworkElement
{
    private static DependencyProperty Paint(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(DiskInstrument), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = Paint(nameof(AccentBrush));
    public static readonly DependencyProperty TrackBrushProperty = Paint(nameof(TrackBrush));
    public static readonly DependencyProperty InkBrushProperty = Paint(nameof(InkBrush));
    public static readonly DependencyProperty MutedBrushProperty = Paint(nameof(MutedBrush));
    public static readonly DependencyProperty FaintBrushProperty = Paint(nameof(FaintBrush));
    public static readonly DependencyProperty SurfaceBrushProperty = Paint(nameof(SurfaceBrush));
    public static readonly DependencyProperty OkBrushProperty = Paint(nameof(OkBrush));
    public static readonly DependencyProperty WarnBrushProperty = Paint(nameof(WarnBrush));
    public static readonly DependencyProperty WarnSoftBrushProperty = Paint(nameof(WarnSoftBrush));
    public static readonly DependencyProperty DangerBrushProperty = Paint(nameof(DangerBrush));
    public static readonly DependencyProperty DangerSoftBrushProperty = Paint(nameof(DangerSoftBrush));
    public static readonly DependencyProperty RaisedBrushProperty = Paint(nameof(RaisedBrush));
    public static readonly DependencyProperty LineStrongBrushProperty = Paint(nameof(LineStrongBrush));

    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush InkBrush { get => (Brush)GetValue(InkBrushProperty); set => SetValue(InkBrushProperty, value); }
    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush FaintBrush { get => (Brush)GetValue(FaintBrushProperty); set => SetValue(FaintBrushProperty, value); }
    public Brush SurfaceBrush { get => (Brush)GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }
    public Brush OkBrush { get => (Brush)GetValue(OkBrushProperty); set => SetValue(OkBrushProperty, value); }
    public Brush WarnBrush { get => (Brush)GetValue(WarnBrushProperty); set => SetValue(WarnBrushProperty, value); }
    public Brush WarnSoftBrush { get => (Brush)GetValue(WarnSoftBrushProperty); set => SetValue(WarnSoftBrushProperty, value); }
    public Brush DangerBrush { get => (Brush)GetValue(DangerBrushProperty); set => SetValue(DangerBrushProperty, value); }
    public Brush DangerSoftBrush { get => (Brush)GetValue(DangerSoftBrushProperty); set => SetValue(DangerSoftBrushProperty, value); }
    public Brush RaisedBrush { get => (Brush)GetValue(RaisedBrushProperty); set => SetValue(RaisedBrushProperty, value); }
    public Brush LineStrongBrush { get => (Brush)GetValue(LineStrongBrushProperty); set => SetValue(LineStrongBrushProperty, value); }

    protected DiskInstrument()
    {
        SnapsToDevicePixels = true;
        SetResourceReference(AccentBrushProperty, "Accent");
        SetResourceReference(TrackBrushProperty, "Line");
        SetResourceReference(InkBrushProperty, "Fg");
        SetResourceReference(MutedBrushProperty, "Muted");
        SetResourceReference(FaintBrushProperty, "Faint");
        SetResourceReference(SurfaceBrushProperty, "Surface");
        SetResourceReference(OkBrushProperty, "Ok");
        SetResourceReference(WarnBrushProperty, "Warn");
        SetResourceReference(WarnSoftBrushProperty, "WarnSoft");
        SetResourceReference(DangerBrushProperty, "Danger");
        SetResourceReference(DangerSoftBrushProperty, "DangerSoft");
        SetResourceReference(RaisedBrushProperty, "Raised");
        SetResourceReference(LineStrongBrushProperty, "LineStrong");
    }

    protected static DependencyProperty Number(string name, Type owner, double initial = double.NaN) =>
        DependencyProperty.Register(name, typeof(double), owner, new FrameworkPropertyMetadata(initial, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Texto em fonte de mostrador (Bahnschrift SemiCondensed, algarismos tabulares) ou na fonte da interface.</summary>
    protected FormattedText Text(string text, double size, Brush brush, bool readout = true, FontWeight? weight = null)
    {
        var family = (TryFindResource(readout ? "Readout" : "UiFont") as FontFamily) ?? new FontFamily("Segoe UI");
        var face = new Typeface(family, FontStyles.Normal, weight ?? FontWeights.Normal, readout ? FontStretches.SemiCondensed : FontStretches.Normal);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected static Pen Stroke(Brush brush, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(brush, thickness) { DashStyle = dash ?? DashStyles.Solid };
        pen.Freeze();
        return pen;
    }
}

/// <summary>
/// Vida útil: arco de 240° em gomos, como um conta-giros. Enche da esquerda para a direita com a vida restante; o trecho
/// final (abaixo de 20 % e de 5 %) tem a faixa de atenção e a crítica marcadas por fora, como a faixa vermelha de um motor.
/// </summary>
public sealed class LifeGauge : DiskInstrument
{
    public static readonly DependencyProperty ValueProperty = Number(nameof(Value), typeof(LifeGauge));
    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(LifeGauge), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Vida restante em % (0–100); NaN quando o disco não informa.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    private const int Segments = 44;
    private const double Sweep = 240, Start = -120;
    private static readonly (double At, string Text)[] Marks = { (0, "0"), (0.2, "20"), (0.5, "50"), (1, "100") };

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double r2 = Math.Min((w - 64) / 2, (h - 52) / 1.5);
        if (r2 < 40) return;
        double r1 = r2 * 0.79, cx = w / 2;
        double total = 1.5 * r2 + 52, cy = (h - total) / 2 + r2 + 31;
        bool known = !double.IsNaN(Value);
        double rem = known ? Math.Clamp(Value, 0, 100) / 100 : 0;
        double warnAt = 1 - DiskDetail.WearWarnAt / 100, critAt = 1 - DiskDetail.WearCritAt / 100;

        Point At(double f, double r)
        {
            double a = (Start + Sweep * f) * Math.PI / 180;
            return new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
        }
        void Arc(double f0, double f1, double r, Pen pen)
        {
            var fig = new PathFigure { StartPoint = At(f0, r) };
            fig.Segments.Add(new ArcSegment(At(f1, r), new Size(r, r), 0, (f1 - f0) * Sweep > 180, SweepDirection.Clockwise, true));
            dc.DrawGeometry(null, pen, new PathGeometry(new[] { fig }));
        }

        // Faixas de veredito por fora do arco
        double rz = r2 + 8;
        Arc(0, critAt, rz, Stroke(DangerBrush, 2.5));
        Arc(critAt + 0.004, warnAt, rz, Stroke(WarnBrush, 2.5));
        Arc(warnAt + 0.004, 1, rz, Stroke(TrackBrush, 1));

        Brush fill = rem <= critAt ? DangerBrush : rem <= warnAt ? WarnBrush : AccentBrush;
        for (int i = 0; i < Segments; i++)
        {
            double f0 = (double)i / Segments, f1 = (i + 0.72) / Segments, mid = (i + 0.5) / Segments;
            bool on = known && mid <= rem;
            Brush brush = on ? fill : mid < critAt ? DangerSoftBrush : mid < warnAt ? WarnSoftBrush : TrackBrush;
            var fig = new PathFigure { StartPoint = At(f0, r1), IsClosed = true };
            fig.Segments.Add(new LineSegment(At(f0, r2), false));
            fig.Segments.Add(new ArcSegment(At(f1, r2), new Size(r2, r2), 0, false, SweepDirection.Clockwise, false));
            fig.Segments.Add(new LineSegment(At(f1, r1), false));
            fig.Segments.Add(new ArcSegment(At(f0, r1), new Size(r1, r1), 0, false, SweepDirection.Counterclockwise, false));
            dc.DrawGeometry(brush, null, new PathGeometry(new[] { fig }));
        }

        foreach (var (f, label) in Marks)
        {
            var t = Text(label, 11, FaintBrush);
            var p = At(f, r2 + 22);
            dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
        }

        // Leitura central
        var value = Text(known ? $"{Value:0}" : "—", Math.Round(r2 * 0.48), InkBrush, weight: FontWeights.Light);
        var unit = Text(known ? "%" : "", Math.Round(r2 * 0.17), MutedBrush);
        double vw = value.Width + (known ? 4 + unit.Width : 0), baseY = cy + r2 * 0.12;
        dc.DrawText(value, new Point(cx - vw / 2, baseY - value.Baseline));
        if (known) dc.DrawText(unit, new Point(cx - vw / 2 + value.Width + 4, baseY - unit.Baseline));
        var caption = Text(Caption, 12, MutedBrush, readout: false);
        dc.DrawText(caption, new Point(cx - caption.Width / 2, baseY + 10));
    }
}

/// <summary>
/// Régua térmica: medidor segmentado de 20 °C até 10 °C além do limite crítico, com a faixa ideal, a de atenção e a
/// crítica sublinhadas e o ponteiro da leitura atual.
/// </summary>
public sealed class ThermalScale : DiskInstrument
{
    public static readonly DependencyProperty ValueProperty = Number(nameof(Value), typeof(ThermalScale));
    public static readonly DependencyProperty WarnAtProperty = Number(nameof(WarnAt), typeof(ThermalScale), 70);
    public static readonly DependencyProperty CritAtProperty = Number(nameof(CritAt), typeof(ThermalScale), 85);

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double WarnAt { get => (double)GetValue(WarnAtProperty); set => SetValue(WarnAtProperty, value); }
    public double CritAt { get => (double)GetValue(CritAtProperty); set => SetValue(CritAtProperty, value); }

    public ThermalScale() => Height = 44;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 80) return;
        const double lo = 20, period = 5, gap = 1.5, barTop = 4, barH = 16;
        double hi = CritAt + 10;
        double X(double v) => (Math.Clamp(v, lo, hi) - lo) / (hi - lo) * w;
        bool known = !double.IsNaN(Value);
        Brush fill = !known ? TrackBrush : Value >= CritAt ? DangerBrush : Value >= WarnAt ? WarnBrush : AccentBrush;

        int n = (int)Math.Floor((w + gap) / period);
        for (int i = 0; i < n; i++)
        {
            double v = lo + (i + 0.5) / n * (hi - lo);
            dc.DrawRectangle(known && v <= Value ? fill : TrackBrush, null, new Rect(i * period, barTop, period - gap, barH));
        }

        // Faixas: ideal, atenção e crítica
        double zoneY = barTop + barH + 4;
        dc.DrawRectangle(OkBrush, null, new Rect(0, zoneY, Math.Max(0, X(WarnAt) - 1), 3));
        dc.DrawRectangle(WarnBrush, null, new Rect(X(WarnAt) + 1, zoneY, Math.Max(0, X(CritAt) - X(WarnAt) - 2), 3));
        dc.DrawRectangle(DangerBrush, null, new Rect(X(CritAt) + 1, zoneY, Math.Max(0, w - X(CritAt) - 1), 3));

        for (double v = lo; v <= hi + 0.1; v += 10)
        {
            var t = Text($"{v:0}", 11, FaintBrush);
            double x = v <= lo ? 0 : v >= hi ? w - t.Width : X(v) - t.Width / 2;
            dc.DrawText(t, new Point(x, zoneY + 8));
        }

        if (known) dc.DrawRectangle(InkBrush, null, new Rect(X(Value) - 1, barTop - 4, 2, barH + 8));
    }
}

/// <summary>Histórico da temperatura (1 amostra/s) com retícula de osciloscópio e a linha tracejada do limite de atenção.</summary>
public sealed class TraceChart : DiskInstrument
{
    public static readonly DependencyProperty WarnAtProperty = Number(nameof(WarnAt), typeof(TraceChart));
    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(TraceChart), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double WarnAt { get => (double)GetValue(WarnAtProperty); set => SetValue(WarnAtProperty, value); }
    /// <summary>Mensagem no lugar da curva quando não há amostras.</summary>
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    /// <summary>Janela do gráfico, em amostras (uma por segundo).</summary>
    public const int Capacity = 300;
    private IReadOnlyList<float?> _samples = Array.Empty<float?>();
    private IReadOnlyList<float?>? _pending;
    private double? _hoverX;

    /// <summary>
    /// Amostras do último 5 min. Enquanto o mouse está sobre o gráfico ele pausa (a leitura sob o cursor não escapa com a
    /// atualização de 1 em 1 segundo) e retoma com o que chegou nesse tempo, como o gráfico da Visão geral.
    /// </summary>
    public IReadOnlyList<float?> Samples
    {
        get => _samples;
        set
        {
            if (_hoverX != null) { _pending = value; return; }
            _samples = value;
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverX = null;
        if (_pending != null) { _samples = _pending; _pending = null; }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 50) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // área clicável para o cursor
        const double left = 34, bottom = 20, top = 6, lo = 20;
        double plotW = w - left, plotH = h - top - bottom;
        var values = _samples.Where(v => v != null).Select(v => (double)v!.Value).ToList();
        double peak = values.Count > 0 ? values.Max() : 0;
        double hi = Math.Max(double.IsNaN(WarnAt) ? 60 : WarnAt + 10, Math.Ceiling((peak + 5) / 10) * 10);
        double X(int i) => left + plotW * (i + Capacity - _samples.Count) / (Capacity - 1);
        double Y(double v) => top + plotH * (1 - (Math.Clamp(v, lo, hi) - lo) / (hi - lo));

        var grid = Stroke(TrackBrush, 1);
        for (int k = 0; k <= 3; k++)
        {
            double v = lo + (hi - lo) * k / 3, y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(grid, new Point(left, y), new Point(w, y));
            var t = Text($"{v:0}°", 11, FaintBrush);
            dc.DrawText(t, new Point(0, y - t.Height / 2));
        }
        for (int k = 0; k <= 4; k++)
        {
            double x = Math.Round(left + plotW * k / 4) + 0.5;
            dc.DrawLine(grid, new Point(x, top), new Point(x, top + plotH));
        }
        string[] xs = { "−5 min", "−2,5 min", "agora" };
        for (int k = 0; k < 3; k++)
        {
            var t = Text(xs[k], 11, FaintBrush);
            double x = k == 0 ? left : k == 2 ? w - t.Width : left + plotW / 2 - t.Width / 2;
            dc.DrawText(t, new Point(x, h - t.Height + 2));
        }
        if (!double.IsNaN(WarnAt))
        {
            double y = Math.Round(Y(WarnAt)) + 0.5;
            dc.DrawLine(Stroke(WarnBrush, 1, new DashStyle(new double[] { 4, 4 }, 0)), new Point(left, y), new Point(w, y));
            var t = Text($"atenção {WarnAt:0} °C", 11, WarnBrush, readout: false);
            dc.DrawText(t, new Point(w - t.Width - 4, y - t.Height - 2));
        }

        var points = new List<Point>();
        for (int i = 0; i < _samples.Count; i++)
            if (_samples[i] is float v) points.Add(new Point(X(i), Y(v)));
        if (points.Count < 2)
        {
            var t = Text(EmptyText, 12, MutedBrush, readout: false);
            t.MaxTextWidth = Math.Max(40, plotW - 40);
            t.TextAlignment = TextAlignment.Center;
            dc.DrawText(t, new Point(left + 20, top + plotH / 2 - t.Height / 2));
            return;
        }

        var color = AccentBrush is SolidColorBrush s ? s.Color : Colors.Teal;
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, top + plotH), true, true);
            g.PolyLineTo(points, true, false);
            g.LineTo(new Point(points[^1].X, top + plotH), false, false);
        }
        area.Freeze();
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(0x44, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90), null, area);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false, false);
            g.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(AccentBrush, 2) { LineJoin = PenLineJoin.Round }, line);
        dc.DrawEllipse(SurfaceBrush, Stroke(AccentBrush, 2), points[^1], 4.5, 4.5);
        DrawHover(dc, w, left, top, plotW, plotH, X, Y);
    }

    /// <summary>Cursor: linha tracejada na amostra mais próxima, um ponto na curva e um quadro com a hora e a temperatura exata.</summary>
    private void DrawHover(DrawingContext dc, double w, double left, double top, double plotW, double plotH, Func<int, double> x, Func<double, double> y)
    {
        if (_hoverX is not double mx || mx < left) return;
        int nearest = -1; double best = double.MaxValue;
        for (int i = 0; i < _samples.Count; i++)
        {
            if (_samples[i] == null) continue;
            double d = Math.Abs(x(i) - mx);
            if (d < best) { best = d; nearest = i; }
        }
        if (nearest < 0 || best > Math.Max(12, plotW / Capacity * 4)) return;

        double px = x(nearest), py = y(_samples[nearest]!.Value);
        double lx = Math.Round(px) + 0.5;
        dc.DrawLine(Stroke(FaintBrush, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(lx, top), new Point(lx, top + plotH));
        dc.DrawEllipse(SurfaceBrush, Stroke(AccentBrush, 2.5), new Point(px, py), 5.5, 5.5);

        int ago = _samples.Count - 1 - nearest;
        string when = ago <= 0 ? "agora" : ago >= 60 ? $"há {ago / 60} min {ago % 60:00} s" : $"há {ago} s";
        var time = Text(when, 11.5, FaintBrush, readout: false);
        var value = Text($"{_samples[nearest]!.Value:0.#} °C", 16, InkBrush);
        double bw = Math.Max(time.Width, value.Width) + 22, bh = time.Height + value.Height + 14;
        double bx = px + 14 + bw > w ? px - 14 - bw : px + 14;
        double by = Math.Clamp(py - bh / 2, top, Math.Max(top, top + plotH - bh));
        dc.DrawRoundedRectangle(RaisedBrush, Stroke(LineStrongBrush, 1), new Rect(bx, by, bw, bh), 8, 8);
        dc.DrawText(time, new Point(bx + 11, by + 7));
        dc.DrawText(value, new Point(bx + 11, by + 7 + time.Height));
    }
}

/// <summary>
/// Espaço do disco por unidade: um trecho por letra, proporcional ao tamanho, com o ocupado em medidor segmentado.
/// O que não tem letra (EFI, recuperação, espaço livre) fica como trilho vazado no fim.
/// </summary>
public sealed class CapacityBar : DiskInstrument
{
    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(CapacityBar), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush FillBrush { get => (Brush)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    private IReadOnlyList<(double Size, double Used)> _volumes = Array.Empty<(double, double)>();
    private double _rest;

    public CapacityBar()
    {
        Height = 16;
        SetResourceReference(FillBrushProperty, "Steel");
    }

    /// <summary>Tamanho e ocupado de cada unidade (mesma escala), mais o que sobra no disco sem letra.</summary>
    public void SetVolumes(IReadOnlyList<(double Size, double Used)> volumes, double rest)
    {
        _volumes = volumes;
        _rest = rest;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double total = _volumes.Sum(v => v.Size) + _rest;
        if (w < 20 || total <= 0) return;
        const double period = 5, gap = 1.5, between = 4;
        int parts = _volumes.Count + (_rest > 0 ? 1 : 0);
        double usable = w - between * (parts - 1), x = 0;
        foreach (var (size, used) in _volumes)
        {
            double width = usable * size / total, filled = width * Math.Clamp(used / size, 0, 1);
            for (double sx = 0; sx + period - gap <= width + 0.01; sx += period)
                dc.DrawRectangle(sx + (period - gap) / 2 <= filled ? FillBrush : TrackBrush, null, new Rect(x + sx, 0, period - gap, h));
            x += width + between;
        }
        if (_rest > 0)
            dc.DrawRoundedRectangle(null, Stroke(TrackBrush, 1), new Rect(x + 0.5, 0.5, Math.Max(0, w - x - 1), h - 1), 2, 2);
    }
}
