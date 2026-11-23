using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CoreBi.Engine.Monitoring.Storage;

namespace CoreBi.Desktop;

/// <summary>Base dos instrumentos da página Armazenamento (canal padrão Storage).</summary>
public abstract class DiskInstrument : InstrumentBase
{
    protected DiskInstrument() => SetResourceReference(ChannelBrushProperty, "Storage");
}

/// <summary>Vida útil em arco de 240° segmentado com faixas de atenção (20%) e crítica (5%).</summary>
public sealed class LifeGauge : DiskInstrument
{
    public static readonly DependencyProperty ValueProperty = Number(nameof(Value), typeof(LifeGauge));
    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(LifeGauge), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    private const int SegmentsCount = 44;
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

        double rz = r2 + 8;
        Arc(0, critAt, rz, Stroke(DangerBrush, 2.5));
        Arc(critAt + 0.004, warnAt, rz, Stroke(WarnBrush, 2.5));
        Arc(warnAt + 0.004, 1, rz, Stroke(TrackBrush, 1));

        Brush fill = rem <= critAt ? DangerBrush : rem <= warnAt ? WarnBrush : ChannelBrush;
        for (int i = 0; i < SegmentsCount; i++)
        {
            double f0 = (double)i / SegmentsCount, f1 = (i + 0.72) / SegmentsCount, mid = (i + 0.5) / SegmentsCount;
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
            var t = Text(label, AxisText, FaintBrush);
            var p = At(f, r2 + 22);
            dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
        }

        var value = Text(known ? $"{Value:0}" : "—", Math.Round(r2 * 0.48), InkBrush, weight: FontWeights.Light);
        var unit = Text(known ? "%" : "", Math.Round(r2 * 0.17), MutedBrush);
        double vw = value.Width + (known ? 4 + unit.Width : 0), baseY = cy + r2 * 0.12;
        dc.DrawText(value, new Point(cx - vw / 2, baseY - value.Baseline));
        if (known) dc.DrawText(unit, new Point(cx - vw / 2 + value.Width + 4, baseY - unit.Baseline));
        var caption = Text(Caption, 12, MutedBrush, readout: false);
        dc.DrawText(caption, new Point(cx - caption.Width / 2, baseY + 10));
    }
}

/// <summary>Régua térmica segmentada de 20 °C até 10 °C além do limite crítico.</summary>
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
        const double lo = 20, barTop = 4, barH = 16;
        double hi = CritAt + 10;
        double X(double v) => (Math.Clamp(v, lo, hi) - lo) / (hi - lo) * w;
        bool known = !double.IsNaN(Value);
        Brush fill = !known ? TrackBrush : Value >= CritAt ? DangerBrush : Value >= WarnAt ? WarnBrush : ChannelBrush;

        Segments(dc, new Rect(0, barTop, w, barH), f => known && lo + f * (hi - lo) <= Value ? fill : TrackBrush);

        double zoneY = barTop + barH + 4;
        dc.DrawRectangle(LineStrongBrush, null, new Rect(0, zoneY, Math.Max(0, X(WarnAt) - 1), 3));
        dc.DrawRectangle(WarnBrush, null, new Rect(X(WarnAt) + 1, zoneY, Math.Max(0, X(CritAt) - X(WarnAt) - 2), 3));
        dc.DrawRectangle(DangerBrush, null, new Rect(X(CritAt) + 1, zoneY, Math.Max(0, w - X(CritAt) - 1), 3));

        // Os números de atenção e crítica ficam no mesmo tom dos outros; a cor da faixa fica só no traço embaixo da barra
        var marks = new[] { WarnAt, CritAt }.Where(at => !double.IsNaN(at) && at > lo && at < hi).ToList();
        double last = lo + Math.Floor((hi - lo) / 10 + 0.01) * 10;
        for (double v = lo; v <= hi + 0.1; v += 10)
        {
            if (marks.Any(at => Math.Abs(X(at) - X(v)) < 26)) continue;
            var t = Text(v >= last - 0.1 ? $"{v:0} °C" : $"{v:0}", AxisText, FaintBrush);
            double x = v <= lo ? 0 : v >= hi ? w - t.Width : X(v) - t.Width / 2;
            dc.DrawText(t, new Point(x, zoneY + 8));
        }
        foreach (double at in marks)
        {
            var t = Text($"{at:0}", AxisText, FaintBrush);
            dc.DrawText(t, new Point(Math.Clamp(X(at) - t.Width / 2, 0, w - t.Width), zoneY + 8));
        }

        if (known) dc.DrawRectangle(InkBrush, null, new Rect(X(Value) - 1, barTop - 4, 2, barH + 8));
    }
}

/// <summary>Histórico da temperatura (1 amostra/s) com retícula, limite de atenção e pausa sob o mouse.</summary>
public sealed class TraceChart : DiskInstrument
{
    public static readonly DependencyProperty WarnAtProperty = Number(nameof(WarnAt), typeof(TraceChart));
    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(TraceChart), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double WarnAt { get => (double)GetValue(WarnAtProperty); set => SetValue(WarnAtProperty, value); }
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    public const int Capacity = 300;
    private IReadOnlyList<float?> _samples = Array.Empty<float?>();
    private IReadOnlyList<float?>? _pending;
    private double? _hoverX;
    private bool _fresh;

    public IReadOnlyList<float?> Samples
    {
        get => _samples;
        set
        {
            if (_hoverX != null) { _pending = value; return; }
            _samples = value;
            _fresh = true;
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
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
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        const double left = 44, bottom = 20, top = 6, lo = 20;
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
            var t = Text($"{v:0} °C", AxisText, FaintBrush);
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
            var t = Text(xs[k], AxisText, FaintBrush);
            double x = k == 0 ? left : k == 2 ? w - t.Width : left + plotW / 2 - t.Width / 2;
            dc.DrawText(t, new Point(x, h - t.Height + 2));
        }
        if (!double.IsNaN(WarnAt))
        {
            double y = Math.Round(Y(WarnAt)) + 0.5;
            dc.DrawLine(Stroke(WarnBrush, 1, new DashStyle(new double[] { 4, 4 }, 0)), new Point(left, y), new Point(w, y));
            var t = Text($"atenção {WarnAt:0} °C", AxisText, WarnBrush, readout: false);
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

        var color = ColorOf(ChannelBrush, Colors.SteelBlue);
        dc.DrawGeometry(Alpha(ChannelBrush, 0x24), null, CreateAreaGeometry(points, top + plotH));
        var pen = new Pen(ChannelBrush, 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, CreatePolylineGeometry(points));
        if (_fresh && _hoverX == null) Pulse(dc, color, points[^1]);
        _fresh = false;
        dc.DrawEllipse(SurfaceBrush, Stroke(ChannelBrush, 2), points[^1], 4.5, 4.5);
        DrawHover(dc, w, left, top, plotW, plotH, X, Y);
    }

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
        dc.DrawEllipse(SurfaceBrush, Stroke(ChannelBrush, 2.5), new Point(px, py), 5.5, 5.5);

        string when = ChartScale.RelativeAgo(_samples.Count - 1 - nearest);
        var time = Text(when, AxisText, FaintBrush, readout: false);
        var value = Text($"{_samples[nearest]!.Value:0.#} °C", 16, InkBrush);
        double bw = Math.Max(time.Width, value.Width) + 24, bh = time.Height + value.Height + 16;
        double bx = px + 14 + bw > w ? px - 14 - bw : px + 14;
        double by = Math.Clamp(py - bh / 2, top, Math.Max(top, top + plotH - bh));
        dc.DrawRoundedRectangle(RaisedBrush, Stroke(LineStrongBrush, 1), new Rect(bx, by, bw, bh), TipRadius, TipRadius);
        dc.DrawText(time, new Point(bx + 12, by + 8));
        dc.DrawText(value, new Point(bx + 12, by + 8 + time.Height));
    }
}

/// <summary>Espaço do disco por unidade: um trecho segmentado por letra mais trilho vazado para partições sem letra.</summary>
public sealed class CapacityBar : DiskInstrument
{
    private IReadOnlyList<(double Size, double Used)> _volumes = Array.Empty<(double, double)>();
    private double _rest;

    public CapacityBar() => Height = 16;

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
        const double between = 4;
        int parts = _volumes.Count + (_rest > 0 ? 1 : 0);
        double usable = w - between * (parts - 1), x = 0;
        foreach (var (size, used) in _volumes)
        {
            double width = usable * size / total, filled = width * Math.Clamp(used / size, 0, 1);
            for (double sx = 0; sx + SegmentPeriod - SegmentGap <= width + 0.01; sx += SegmentPeriod)
                dc.DrawRectangle(sx + (SegmentPeriod - SegmentGap) / 2 <= filled ? ChannelBrush : TrackBrush, null,
                    new Rect(x + sx, 0, SegmentPeriod - SegmentGap, h));
            x += width + between;
        }
        if (_rest > 0)
            dc.DrawRoundedRectangle(null, Stroke(TrackBrush, 1), new Rect(x + 0.5, 0.5, Math.Max(0, w - x - 1), h - 1), 2, 2);
    }
}
