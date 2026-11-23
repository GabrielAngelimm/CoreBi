using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Traço de FPS ao longo de um benchmark (eixo X = progresso 0..1 informado pelo motor), com linha tracejada da média. Parado,
/// mostra a última passada guardada (Load), com a nota de quando foi no canto de cima.
/// </summary>
public sealed class RunTrace : InstrumentBase
{
    private readonly List<Point> _samples = new();
    private double? _hoverX;

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(RunTrace), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty NoteProperty =
        DependencyProperty.Register(nameof(Note), typeof(string), typeof(RunTrace), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(RunTrace), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    /// <summary>Uma linha no canto de cima, à direita ("Última passada · hoje às 21:40"); vazia durante o teste.</summary>
    public string Note { get => (string)GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

    public void Clear()
    {
        _samples.Clear();
        _frozenCount = null;
        Note = "";
        InvalidateVisual();
    }

    /// <summary>Mostra uma passada guardada: os valores em progresso igual do início ao fim.</summary>
    public void Load(IReadOnlyList<double>? values, string note)
    {
        _samples.Clear();
        _frozenCount = null;
        if (values is { Count: >= 2 })
            for (int i = 0; i < values.Count; i++)
                if (double.IsFinite(values[i]) && values[i] > 0) _samples.Add(new Point((double)i / (values.Count - 1), values[i]));
        Note = _samples.Count >= 2 ? note : "";
        InvalidateVisual();
    }

    /// <summary>A passada atual reamostrada em até <paramref name="count"/> pontos de progresso igual, para guardar.</summary>
    public List<double>? Snapshot(int count = 120)
    {
        if (_samples.Count < 2) return null;
        var list = new List<double>(count);
        int j = 0;
        double first = _samples[0].X, last = _samples[^1].X;
        for (int i = 0; i < count; i++)
        {
            double x = first + (last - first) * i / (count - 1);
            while (j < _samples.Count - 2 && _samples[j + 1].X < x) j++;
            var (a, b) = (_samples[j], _samples[j + 1]);
            double t = b.X > a.X ? Math.Clamp((x - a.X) / (b.X - a.X), 0, 1) : 0;
            list.Add(Math.Round(a.Y + (b.Y - a.Y) * t, 1));
        }
        return list;
    }

    public void Add(double progress, double value)
    {
        if (!double.IsFinite(value) || value <= 0 || !double.IsFinite(progress)) return;
        progress = Math.Clamp(progress, 0, 1);
        if (_samples.Count > 0 && progress < _samples[^1].X) return;
        _samples.Add(new Point(progress, value));
        InvalidateVisual();
    }

    // Com o mouse em cima, o traço congela no que já tinha (como os outros gráficos): os pontos novos entram ao tirar
    private int? _frozenCount;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _frozenCount ??= _samples.Count;
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverX = null;
        _frozenCount = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 120 || h < 80) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        const double left = 44, bottom = 20;
        string note = Note ?? "";
        double top = note.Length > 0 ? 28 : 12;
        double plotW = w - left, plotH = h - top - bottom;
        var samples = _frozenCount is int frozen && frozen <= _samples.Count ? _samples.GetRange(0, frozen) : _samples;

        double peak = samples.Count > 0 ? samples.Max(p => p.Y) : 0;
        double step = peak <= 40 ? 10 : peak <= 100 ? 20 : peak <= 250 ? 50 : peak <= 500 ? 100 : peak <= 1000 ? 200 : 500;
        double hi = Math.Max(step * 3, Math.Ceiling(peak * 1.12 / step) * step);
        double X(double p) => left + plotW * p;
        double Y(double v) => top + plotH * (1 - Math.Clamp(v, 0, hi) / hi);

        var grid = Stroke(TrackBrush, 1);
        for (double v = 0; v <= hi + 0.1; v += step)
        {
            double y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(grid, new Point(left, y), new Point(w, y));
            var t = Text($"{v:0}", 12, FaintBrush);
            // Meio dos algarismos no fio (base menos metade da altura das maiúsculas, 0,7 em).
            dc.DrawText(t, new Point(left - 10 - t.Width, y - (t.Baseline - 12 * 0.7 / 2)));
        }
        var start = Text("início", 12, FaintBrush);
        var end = Text("fim", 12, FaintBrush);
        if (note.Length > 0)
        {
            var n = Text(note, 12, MutedBrush, readout: false);
            dc.DrawText(n, new Point(w - n.Width, 0));
        }
        dc.DrawText(start, new Point(left, h - start.Height + 2));
        dc.DrawText(end, new Point(w - end.Width, h - end.Height + 2));

        if (samples.Count < 2)
        {
            var t = Text(EmptyText, 13, MutedBrush, readout: false);
            t.MaxTextWidth = Math.Max(40, plotW - 40);
            t.TextAlignment = TextAlignment.Center;
            dc.DrawText(t, new Point(left + 20, top + plotH / 2 - t.Height / 2));
            return;
        }

        var points = samples.Select(s => new Point(X(s.X), Y(s.Y))).ToList();
        dc.DrawGeometry(Alpha(ChannelBrush, 0x30), null, CreateAreaGeometry(points, top + plotH));
        var pen = new Pen(ChannelBrush, 1.8) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, CreatePolylineGeometry(points));

        double mean = samples.Average(s => s.Y);
        double my = Math.Round(Y(mean)) + 0.5;
        dc.DrawLine(Stroke(MutedBrush, 1, new DashStyle(new double[] { 4, 3 }, 0)), new Point(left, my), new Point(points[^1].X, my));
        var label = Text($"média {mean:0} {Unit}".TrimEnd(), 12, MutedBrush);
        double lx = Math.Max(left + 6, Math.Min(points[^1].X, w) - label.Width - 6);
        dc.DrawRectangle(SurfaceBrush, null, new Rect(lx - 4, my - label.Height - 2, label.Width + 8, label.Height));
        dc.DrawText(label, new Point(lx, my - label.Height - 2));

        dc.DrawEllipse(SurfaceBrush, Stroke(ChannelBrush, 2), points[^1], 4, 4);
        DrawHover(dc, w, top, plotH, samples, points);
    }

    /// <summary>A leitura sob o mouse, no desenho da dica dos gráficos de Discos e Energia: fio, ponto e caixa com o valor.</summary>
    private void DrawHover(DrawingContext dc, double w, double top, double plotH, List<Point> samples, List<Point> points)
    {
        if (_hoverX is not double mx) return;
        int nearest = -1;
        double best = double.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            double d = Math.Abs(points[i].X - mx);
            if (d < best) { best = d; nearest = i; }
        }
        if (nearest < 0 || best > 16) return;

        var p = points[nearest];
        double lx = Math.Round(p.X) + 0.5;
        dc.DrawLine(Stroke(FaintBrush, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(lx, top), new Point(lx, top + plotH));
        dc.DrawEllipse(SurfaceBrush, Stroke(ChannelBrush, 2.5), p, 5.5, 5.5);

        var when = Text($"aos {samples[nearest].X * 100:0} % da passada", 11.5, FaintBrush, readout: false);
        var value = Text($"{samples[nearest].Y:0} {Unit}".TrimEnd(), 16, InkBrush);
        double bw = Math.Max(when.Width, value.Width) + 24, bh = when.Height + value.Height + 16;
        double bx = p.X + 14 + bw > w ? p.X - 14 - bw : p.X + 14;
        double by = Math.Clamp(p.Y - bh / 2, top, Math.Max(top, top + plotH - bh));
        dc.DrawRoundedRectangle(RaisedBrush, Stroke(LineStrongBrush, 1), new Rect(bx, by, bw, bh), 8, 8);
        dc.DrawText(when, new Point(bx + 12, by + 8));
        dc.DrawText(value, new Point(bx + 12, by + 8 + when.Height));
    }
}
