using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Base dos instrumentos da página Energia: a mesma dos instrumentos de Discos (pincéis por referência de recurso, que
/// acompanham o tema sozinhos) com os canais B (cobre, placa de vídeo) e o neutro técnico (aço).
/// </summary>
public abstract class EnergyInstrument : DiskInstrument
{
    public static readonly DependencyProperty CopperBrushProperty =
        DependencyProperty.Register(nameof(CopperBrush), typeof(Brush), typeof(EnergyInstrument), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SteelBrushProperty =
        DependencyProperty.Register(nameof(SteelBrush), typeof(Brush), typeof(EnergyInstrument), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush CopperBrush { get => (Brush)GetValue(CopperBrushProperty); set => SetValue(CopperBrushProperty, value); }
    public Brush SteelBrush { get => (Brush)GetValue(SteelBrushProperty); set => SetValue(SteelBrushProperty, value); }

    protected EnergyInstrument()
    {
        SetResourceReference(CopperBrushProperty, "Copper");
        SetResourceReference(SteelBrushProperty, "Steel");
    }

    protected const double Period = 5, Gap = 1.5;

    /// <summary>Medidor segmentado (como um VU): um gomo a cada 5 px; color decide a cor de cada gomo pela posição (0–1).</summary>
    protected void Segments(DrawingContext dc, Rect area, Func<double, Brush> color)
    {
        int n = (int)Math.Floor((area.Width + Gap) / Period);
        for (int i = 0; i < n; i++)
            dc.DrawRectangle(color((i + 0.5) / n), null, new Rect(area.X + i * Period, area.Y, Period - Gap, area.Height));
    }

    protected static Color ColorOf(Brush brush, Color fallback) => brush is SolidColorBrush s ? s.Color : fallback;
    protected static Brush Alpha(Brush brush, byte alpha)
    {
        var c = ColorOf(brush, Colors.Gray);
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}

/// <summary>
/// Consumo dos últimos 5 min (1 amostra/s), empilhado: processador (verde-água) embaixo e placa de vídeo (cobre) por cima,
/// então o contorno superior é a soma. Pausa sob o mouse e mostra a hora e cada parcela, como os gráficos de Discos e Visão geral.
/// </summary>
public sealed class PowerTrace : EnergyInstrument
{
    public const int Capacity = 300;
    private IReadOnlyList<(float? Cpu, float? Gpu)> _samples = Array.Empty<(float?, float?)>();
    private IReadOnlyList<(float? Cpu, float? Gpu)>? _pending;
    private double? _hoverX;

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(PowerTrace), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    public IReadOnlyList<(float? Cpu, float? Gpu)> Samples
    {
        get => _samples;
        set
        {
            if (_hoverX != null) { _pending = value; return; }
            _samples = value;
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

    private static double Total((float? Cpu, float? Gpu) s) => (s.Cpu ?? 0) + (s.Gpu ?? 0);
    private static bool Known((float? Cpu, float? Gpu) s) => s.Cpu != null || s.Gpu != null;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 120 || h < 80) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        const double left = 44, bottom = 20, top = 10;
        double plotW = w - left, plotH = h - top - bottom;
        double peak = _samples.Where(Known).Select(Total).DefaultIfEmpty(0).Max();
        double step = peak <= 80 ? 20 : peak <= 200 ? 50 : peak <= 400 ? 100 : 200;
        double hi = Math.Max(step * 4, Math.Ceiling(peak * 1.15 / step) * step);
        double X(int i) => left + plotW * (i + Capacity - _samples.Count) / (Capacity - 1);
        double Y(double v) => top + plotH * (1 - Math.Clamp(v, 0, hi) / hi);

        var grid = Stroke(TrackBrush, 1);
        for (double v = 0; v <= hi + 0.1; v += step)
        {
            double y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(grid, new Point(left, y), new Point(w, y));
            var t = Text($"{v:0} W", 11, FaintBrush);
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

        // Trechos contínuos com leitura (uma falha do sensor interrompe a curva em vez de desenhar zero)
        var runs = new List<List<int>>();
        for (int i = 0; i < _samples.Count; i++)
        {
            if (!Known(_samples[i])) continue;
            if (runs.Count == 0 || runs[^1][^1] != i - 1) runs.Add(new List<int>());
            runs[^1].Add(i);
        }
        if (!runs.Any(r => r.Count >= 2))
        {
            var t = Text(EmptyText, 12, MutedBrush, readout: false);
            t.MaxTextWidth = Math.Max(40, plotW - 40);
            t.TextAlignment = TextAlignment.Center;
            dc.DrawText(t, new Point(left + 20, top + plotH / 2 - t.Height / 2));
            return;
        }

        Color a = ColorOf(AccentBrush, Colors.Teal), b = ColorOf(CopperBrush, Colors.Peru);
        Brush Fade(Color c, byte from, byte to)
        {
            var g = new LinearGradientBrush(Color.FromArgb(from, c.R, c.G, c.B), Color.FromArgb(to, c.R, c.G, c.B), 90);
            g.Freeze();
            return g;
        }
        Brush cpuFill = Fade(a, 0x58, 0x12), gpuFill = Fade(b, 0x4C, 0x0E);
        var cpuPen = new Pen(AccentBrush, 1.6) { LineJoin = PenLineJoin.Round };
        var gpuPen = new Pen(CopperBrush, 2) { LineJoin = PenLineJoin.Round };
        double floor = top + plotH;
        foreach (var run in runs.Where(r => r.Count >= 2))
        {
            var lower = run.Select(i => new Point(X(i), Y(_samples[i].Cpu ?? 0))).ToList();
            var upper = run.Select(i => new Point(X(i), Y(Total(_samples[i])))).ToList();
            bool hasCpu = run.Any(i => _samples[i].Cpu != null), hasGpu = run.Any(i => _samples[i].Gpu != null);
            if (hasGpu) dc.DrawGeometry(gpuFill, null, Band(upper, lower));
            if (hasCpu) dc.DrawGeometry(cpuFill, null, Band(lower, lower.Select(p => new Point(p.X, floor)).ToList()));
            if (hasCpu) dc.DrawGeometry(null, cpuPen, Polyline(lower));
            if (hasGpu) dc.DrawGeometry(null, gpuPen, Polyline(upper));
        }

        // Pico da janela, marcado sobre a curva
        int pi = -1; double pv = 0;
        for (int i = 0; i < _samples.Count; i++)
            if (Known(_samples[i]) && Total(_samples[i]) > pv) { pv = Total(_samples[i]); pi = i; }
        if (pi >= 0 && _hoverX == null)
        {
            double px = X(pi), py = Y(pv);
            dc.DrawLine(Stroke(FaintBrush, 1), new Point(Math.Round(px) + 0.5, py - 4), new Point(Math.Round(px) + 0.5, py - 14));
            var t = Text($"pico {pv:0} W", 11, MutedBrush);
            dc.DrawText(t, new Point(Math.Clamp(px - t.Width / 2, left, w - t.Width), Math.Max(0, py - 16 - t.Height)));
        }

        var last = _samples.Count - 1;
        if (Known(_samples[last]))
        {
            var lx = X(last);
            if (_samples[last].Cpu is float c) dc.DrawEllipse(SurfaceBrush, Stroke(AccentBrush, 2), new Point(lx, Y(c)), 4, 4);
            if (_samples[last].Gpu != null) dc.DrawEllipse(SurfaceBrush, Stroke(CopperBrush, 2), new Point(lx, Y(Total(_samples[last]))), 4.5, 4.5);
        }
        DrawHover(dc, w, left, top, plotW, plotH, X, Y);
    }

    private static Geometry Band(IList<Point> upper, IList<Point> lower)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(upper[0], true, true);
            ctx.PolyLineTo(upper.Skip(1).ToList(), true, false);
            ctx.PolyLineTo(lower.Reverse().ToList(), true, false);
        }
        g.Freeze();
        return g;
    }

    private static Geometry Polyline(IList<Point> points)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    private void DrawHover(DrawingContext dc, double w, double left, double top, double plotW, double plotH, Func<int, double> x, Func<double, double> y)
    {
        if (_hoverX is not double mx || mx < left) return;
        int nearest = -1; double best = double.MaxValue;
        for (int i = 0; i < _samples.Count; i++)
        {
            if (!Known(_samples[i])) continue;
            double d = Math.Abs(x(i) - mx);
            if (d < best) { best = d; nearest = i; }
        }
        if (nearest < 0 || best > Math.Max(12, plotW / Capacity * 4)) return;
        var s = _samples[nearest];
        double px = x(nearest), lx = Math.Round(px) + 0.5;
        dc.DrawLine(Stroke(FaintBrush, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(lx, top), new Point(lx, top + plotH));
        if (s.Cpu is float c) dc.DrawEllipse(SurfaceBrush, Stroke(AccentBrush, 2.5), new Point(px, y(c)), 5, 5);
        if (s.Gpu != null) dc.DrawEllipse(SurfaceBrush, Stroke(CopperBrush, 2.5), new Point(px, y(Total(s))), 5.5, 5.5);

        int ago = _samples.Count - 1 - nearest;
        string when = ago <= 0 ? "agora" : ago >= 60 ? $"há {ago / 60} min {ago % 60:00} s" : $"há {ago} s";
        var time = Text(when, 11.5, FaintBrush, readout: false);
        var total = Text($"{Total(s):0.#} W", 16, InkBrush);
        var rows = new List<(Brush Dot, FormattedText Label, FormattedText Value)>();
        if (s.Cpu is float cv) rows.Add((AccentBrush, Text("Processador", 12, MutedBrush, readout: false), Text($"{cv:0.#} W", 12.5, InkBrush)));
        if (s.Gpu is float gv) rows.Add((CopperBrush, Text("Placa de vídeo", 12, MutedBrush, readout: false), Text($"{gv:0.#} W", 12.5, InkBrush)));
        double labelW = rows.Count > 0 ? rows.Max(r => r.Label.Width) : 0, valueW = rows.Count > 0 ? rows.Max(r => r.Value.Width) : 0;
        double rowH = 18, bw = Math.Max(Math.Max(time.Width, total.Width), 14 + labelW + 14 + valueW) + 24;
        double bh = time.Height + total.Height + rows.Count * rowH + 18;
        double bx = px + 14 + bw > w ? px - 14 - bw : px + 14;
        double by = Math.Clamp(y(Total(s)) - bh / 2, top, Math.Max(top, top + plotH - bh));
        dc.DrawRoundedRectangle(RaisedBrush, Stroke(LineStrongBrush, 1), new Rect(bx, by, bw, bh), 8, 8);
        dc.DrawText(time, new Point(bx + 12, by + 8));
        dc.DrawText(total, new Point(bx + 12, by + 8 + time.Height));
        double ry = by + 12 + time.Height + total.Height;
        foreach (var (dot, label, value) in rows)
        {
            dc.DrawRoundedRectangle(dot, null, new Rect(bx + 12, ry + rowH / 2 - 2, 9, 3), 1.5, 1.5);
            dc.DrawText(label, new Point(bx + 26, ry + (rowH - label.Height) / 2));
            dc.DrawText(value, new Point(bx + bw - 12 - value.Width, ry + (rowH - value.Height) / 2));
            ry += rowH;
        }
    }
}

/// <summary>
/// Trilho da fonte: escala de −10 % a +10 % em volta do nominal, com a tolerância ATX (±5 %) acesa em verde suave, o traço
/// do nominal, a faixa vista desde que o CoreBi abriu (embaixo) e o ponteiro da leitura (âmbar fora da tolerância).
/// </summary>
public sealed class RailGauge : EnergyInstrument
{
    public static readonly DependencyProperty NominalProperty = Number(nameof(Nominal), typeof(RailGauge));
    public static readonly DependencyProperty ValueProperty = Number(nameof(Value), typeof(RailGauge));
    public static readonly DependencyProperty MinProperty = Number(nameof(Min), typeof(RailGauge));
    public static readonly DependencyProperty MaxProperty = Number(nameof(Max), typeof(RailGauge));
    public static readonly DependencyProperty ShowScaleProperty =
        DependencyProperty.Register(nameof(ShowScale), typeof(bool), typeof(RailGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Nominal { get => (double)GetValue(NominalProperty); set => SetValue(NominalProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public bool ShowScale { get => (bool)GetValue(ShowScaleProperty); set => SetValue(ShowScaleProperty, value); }

    private const double Span = 0.10, Tol = CoreBi.Engine.Monitoring.Power.Rail.Tolerance;

    protected override Size MeasureOverride(Size available) => new(0, ShowScale ? 30 : 18);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 60 || double.IsNaN(Nominal) || Nominal <= 0) return;
        double lo = Nominal * (1 - Span), hi = Nominal * (1 + Span);
        double X(double v) => (Math.Clamp(v, lo, hi) - lo) / (hi - lo) * w;
        const double y0 = 3, bh = 10;
        var band = Alpha(OkBrush, 0x55);
        Segments(dc, new Rect(0, y0, w, bh), f => Math.Abs(f * 2 - 1) * Span <= Tol ? band : TrackBrush);

        if (!double.IsNaN(Min) && !double.IsNaN(Max))
        {
            double x0 = X(Min), x1 = X(Max);
            dc.DrawRoundedRectangle(Alpha(MutedBrush, 0xB0), null, new Rect(x0, y0 + bh + 3, Math.Max(2, x1 - x0), 2), 1, 1);
        }
        dc.DrawRectangle(FaintBrush, null, new Rect(Math.Round(X(Nominal)) - 0.5, y0 - 2, 1, bh + 4));
        if (!double.IsNaN(Value))
        {
            bool off = Math.Abs(Value / Nominal - 1) > Tol;
            dc.DrawRoundedRectangle(off ? WarnBrush : InkBrush, null, new Rect(X(Value) - 1.25, y0 - 3, 2.5, bh + 6), 1, 1);
        }
        if (!ShowScale) return;
        foreach (var (v, label) in new[] { (Nominal * (1 - Tol), "−5 %"), (Nominal, "nominal"), (Nominal * (1 + Tol), "+5 %") })
        {
            var t = Text(label, 10.5, FaintBrush);
            dc.DrawText(t, new Point(Math.Clamp(X(v) - t.Width / 2, 0, w - t.Width), y0 + bh + 7));
        }
    }
}

/// <summary>Tensão de componente numa escala fixa: trilho fino, a faixa vista na sessão e o ponto da leitura (âmbar quando baixa).</summary>
public sealed class RangeStrip : EnergyInstrument
{
    public static readonly DependencyProperty ValueProperty = Number(nameof(Value), typeof(RangeStrip));
    public static readonly DependencyProperty MinProperty = Number(nameof(Min), typeof(RangeStrip));
    public static readonly DependencyProperty MaxProperty = Number(nameof(Max), typeof(RangeStrip));
    public static readonly DependencyProperty LoProperty = Number(nameof(Lo), typeof(RangeStrip), 0);
    public static readonly DependencyProperty HiProperty = Number(nameof(Hi), typeof(RangeStrip), 1);
    public static readonly DependencyProperty LowProperty =
        DependencyProperty.Register(nameof(Low), typeof(bool), typeof(RangeStrip), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public double Lo { get => (double)GetValue(LoProperty); set => SetValue(LoProperty, value); }
    public double Hi { get => (double)GetValue(HiProperty); set => SetValue(HiProperty, value); }
    public bool Low { get => (bool)GetValue(LowProperty); set => SetValue(LowProperty, value); }

    public RangeStrip() => Height = 14;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, cy = ActualHeight / 2;
        if (w < 30 || Hi <= Lo) return;
        const double pad = 5;
        double X(double v) => pad + (Math.Clamp(v, Lo, Hi) - Lo) / (Hi - Lo) * (w - 2 * pad);
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(pad, cy - 1, w - 2 * pad, 2), 1, 1);
        if (!double.IsNaN(Min) && !double.IsNaN(Max) && Max > Min)
            dc.DrawRoundedRectangle(Alpha(SteelBrush, 0x90), null, new Rect(X(Min), cy - 2, Math.Max(2, X(Max) - X(Min)), 4), 2, 2);
        if (!double.IsNaN(Value))
            dc.DrawEllipse(SurfaceBrush, Stroke(Low ? WarnBrush : InkBrush, 2), new Point(X(Value), cy), 4, 4);
    }
}

/// <summary>Faixa de 0 a 100 % com o trecho entre o mínimo e o máximo aceso (estado do processador no plano).</summary>
public sealed class SpanMeter : EnergyInstrument
{
    public static readonly DependencyProperty FromProperty = Number(nameof(From), typeof(SpanMeter));
    public static readonly DependencyProperty ToProperty = Number(nameof(To), typeof(SpanMeter));
    public double From { get => (double)GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public double To { get => (double)GetValue(ToProperty); set => SetValue(ToProperty, value); }

    public SpanMeter() => Height = 8;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 20) return;
        bool known = !double.IsNaN(From) && !double.IsNaN(To);
        Segments(dc, new Rect(0, 0, w, ActualHeight), f => known && f * 100 >= From - 0.5 && f * 100 <= To + 0.5 ? AccentBrush : TrackBrush);
    }
}

/// <summary>Bateria desenhada: corpo com gomos de carga e o polo à direita. Âmbar abaixo de 20 % e vermelho abaixo de 10 % fora da tomada.</summary>
public sealed class BatteryMeter : EnergyInstrument
{
    public static readonly DependencyProperty ValueProperty = Number(nameof(Value), typeof(BatteryMeter));
    public static readonly DependencyProperty OnPowerProperty =
        DependencyProperty.Register(nameof(OnPower), typeof(bool), typeof(BatteryMeter), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool OnPower { get => (bool)GetValue(OnPowerProperty); set => SetValue(OnPowerProperty, value); }

    public BatteryMeter() => Height = 34;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 60) return;
        const double nub = 5, pad = 4;
        var body = new Rect(0.75, 0.75, w - nub - 3, h - 1.5);
        dc.DrawRoundedRectangle(null, Stroke(LineStrongBrush, 1.5), body, 6, 6);
        dc.DrawRoundedRectangle(LineStrongBrush, null, new Rect(w - nub, h * 0.3, nub, h * 0.4), 2, 2);
        bool known = !double.IsNaN(Value);
        double v = known ? Math.Clamp(Value, 0, 100) : 0;
        Brush fill = !OnPower && v < 10 ? DangerBrush : !OnPower && v < 20 ? WarnBrush : AccentBrush;
        var inner = new Rect(body.X + pad, body.Y + pad, body.Width - 2 * pad, body.Height - 2 * pad);
        int n = (int)Math.Floor((inner.Width + 2) / 8);
        double seg = (inner.Width + 2) / n;
        for (int i = 0; i < n; i++)
            dc.DrawRoundedRectangle(known && (i + 0.5) / n * 100 <= v ? fill : TrackBrush, null, new Rect(inner.X + i * seg, inner.Y, seg - 2, inner.Height), 1.5, 1.5);
    }
}

/// <summary>
/// Capacidade da bateria: o contorno é a capacidade de fábrica, o aço é a capacidade máxima de hoje (o que o desgaste deixou)
/// e o verde-água é a carga agora. A diferença entre o contorno e o aço é o desgaste.
/// </summary>
public sealed class CapacityStack : EnergyInstrument
{
    public static readonly DependencyProperty DesignProperty = Number(nameof(Design), typeof(CapacityStack));
    public static readonly DependencyProperty FullProperty = Number(nameof(Full), typeof(CapacityStack));
    public static readonly DependencyProperty NowProperty = Number(nameof(Now), typeof(CapacityStack));
    public double Design { get => (double)GetValue(DesignProperty); set => SetValue(DesignProperty, value); }
    public double Full { get => (double)GetValue(FullProperty); set => SetValue(FullProperty, value); }
    public double Now { get => (double)GetValue(NowProperty); set => SetValue(NowProperty, value); }

    public CapacityStack() => Height = 12;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double max = Math.Max(double.IsNaN(Design) ? 0 : Design, double.IsNaN(Full) ? 0 : Full);
        if (w < 40 || max <= 0) return;
        double full = double.IsNaN(Full) ? 0 : Full / max, now = double.IsNaN(Now) ? 0 : Now / max;
        Segments(dc, new Rect(0, 0, w, h), f => f <= now ? AccentBrush : f <= full ? Alpha(SteelBrush, 0x8C) : TrackBrush);
    }
}

/// <summary>
/// Quando o PC ficou ligado, um dia por linha (24 h): trechos ligados em verde-água, a sessão atual terminando num ponto e,
/// em vermelho, cada desligamento inesperado. Dias que o registro de eventos já não cobre ficam tracejados. Sob o mouse, a linha
/// mostra os horários de cada trecho.
/// </summary>
public sealed class UptimeChart : EnergyInstrument
{
    public sealed record Day(string Label, bool Today, bool Covered, IReadOnlyList<(double From, double To, bool Current)> Spans, IReadOnlyList<double> Crashes, string Total, IReadOnlyList<string> Detail, string Range);

    private IReadOnlyList<Day> _days = Array.Empty<Day>();
    private int _hover = -1;
    public const double MinRowHeight = 25, MaxRowHeight = 52, AxisHeight = 18;

    /// <summary>As linhas dividem a altura que o painel der (entre o mínimo e o máximo); a altura que sobra fica embaixo.</summary>
    private double RowHeight => _days.Count == 0 ? MinRowHeight : Math.Clamp((ActualHeight - AxisHeight) / _days.Count, MinRowHeight, MaxRowHeight);

    public IReadOnlyList<Day> Days
    {
        get => _days;
        set { _days = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    protected override Size MeasureOverride(Size available) => new(0, _days.Count * MinRowHeight + AxisHeight);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int row = (int)(e.GetPosition(this).Y / RowHeight);
        row = row >= 0 && row < _days.Count ? row : -1;
        if (row != _hover) { _hover = row; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w < 160 || _days.Count == 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));
        double rowH = RowHeight;
        double barH = Math.Clamp(rowH * 0.3, 8, 14);
        const double labelW = 58, totalW = 66, rangeW = 112;
        bool showRange = w >= 520;
        double x0 = labelW, bw = w - labelW - totalW - 12 - (showRange ? rangeW : 0);
        double X(double hour) => x0 + Math.Clamp(hour, 0, 24) / 24 * bw;
        var tick = Alpha(LineStrongBrush, 0xA0);
        var dash = Stroke(LineStrongBrush, 1, new DashStyle(new double[] { 2, 3 }, 0));

        for (int r = 0; r < _days.Count; r++)
        {
            var d = _days[r];
            double cy = r * rowH + rowH / 2, by = cy - barH / 2;
            if (r == _hover) dc.DrawRoundedRectangle(Alpha(InkBrush, 0x0C), null, new Rect(-6, r * rowH + 1, w + 6, rowH - 2), 6, 6);
            var label = Text(d.Label, 12, d.Today ? InkBrush : MutedBrush, readout: false, d.Today ? FontWeights.SemiBold : null);
            dc.DrawText(label, new Point(0, cy - label.Height / 2));

            if (!d.Covered)
            {
                dc.DrawLine(dash, new Point(x0, cy + 0.5), new Point(x0 + bw, cy + 0.5));
                var none = Text("sem registro", 12, FaintBrush, readout: false);
                dc.DrawText(none, new Point(w - none.Width, cy - none.Height / 2));
                continue;
            }
            dc.DrawRoundedRectangle(Alpha(TrackBrush, 0xC0), null, new Rect(x0, by, bw, barH), 2, 2);
            for (int k = 1; k < 4; k++) dc.DrawRectangle(tick, null, new Rect(Math.Round(X(k * 6)) - 0.5, by - 2, 1, barH + 4));
            foreach (var (from, to, current) in d.Spans)
            {
                double a = X(from), b = Math.Max(a + 2, X(to));
                dc.DrawRoundedRectangle(current ? AccentBrush : Alpha(AccentBrush, 0xC8), null, new Rect(a, by, b - a, barH), 2, 2);
                if (current) dc.DrawEllipse(SurfaceBrush, Stroke(AccentBrush, 2), new Point(b, cy), 4, 4);
            }
            foreach (double c in d.Crashes)
            {
                double cx = X(c);
                var tri = new StreamGeometry();
                using (var g = tri.Open())
                {
                    g.BeginFigure(new Point(cx - 4.5, by - 6), true, true);
                    g.LineTo(new Point(cx + 4.5, by - 6), false, false);
                    g.LineTo(new Point(cx, by + 1), false, false);
                }
                tri.Freeze();
                dc.DrawGeometry(DangerBrush, null, tri);
            }
            var total = Text(d.Total, 12.5, InkBrush);
            dc.DrawText(total, new Point(w - total.Width, cy - total.Height / 2));
            if (showRange && d.Range.Length > 0)
            {
                var range = Text(d.Range, 12, MutedBrush);
                dc.DrawText(range, new Point(w - totalW - 8 - range.Width, cy - range.Height / 2));
            }
        }

        double ay = _days.Count * rowH + 3;
        for (int k = 0; k <= 4; k++)
        {
            var t = Text($"{k * 6} h", 11, FaintBrush);
            double x = k == 0 ? x0 : k == 4 ? x0 + bw - t.Width : X(k * 6) - t.Width / 2;
            dc.DrawText(t, new Point(x, ay));
        }

        if (_hover >= 0 && _days[_hover] is { Covered: true, Detail.Count: > 0 } day) DrawDetail(dc, day, _hover, w, x0, bw, rowH);
    }

    /// <summary>Quadro com os horários de cada trecho do dia sob o mouse, acima ou abaixo da linha.</summary>
    private void DrawDetail(DrawingContext dc, Day day, int row, double w, double x0, double bw, double rowH)
    {
        var lines = day.Detail.Select(s => Text(s, 12, InkBrush)).ToList();
        double lh = 18, bwid = lines.Max(l => l.Width) + 24, bh = lines.Count * lh + 14;
        double bx = Math.Min(x0 + bw - bwid, x0 + bw / 2 - bwid / 2);
        bx = Math.Max(x0, bx);
        double rowTop = row * rowH;
        double by = rowTop - bh - 4 >= 0 ? rowTop - bh - 4 : rowTop + rowH + 4;
        dc.DrawRoundedRectangle(RaisedBrush, Stroke(LineStrongBrush, 1), new Rect(bx, by, bwid, bh), 8, 8);
        for (int i = 0; i < lines.Count; i++) dc.DrawText(lines[i], new Point(bx + 12, by + 7 + i * lh + (lh - lines[i].Height) / 2));
    }
}
