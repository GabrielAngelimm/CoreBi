using System.Windows;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Leitura ao longo de um teste (FPS da cena de jogo e do ray tracing): o eixo horizontal é o progresso informado pelo motor
/// (0 a 1), então o traço cresce junto com a barra do palco e fica como o perfil do teste depois que ele termina. Área chapada
/// e traço no canal do componente (ChannelBrush), grade em fio fino, a média dos pontos desenhados numa linha tracejada com
/// rótulo direto. Sem pulso nem animação: o traço é redesenhado só quando chega uma leitura, a cada 250 ms do teste.
/// </summary>
public sealed class RunTrace : EnergyInstrument
{
    private readonly List<Point> _samples = new(); // X = progresso (0–1), Y = leitura

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(RunTrace), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(RunTrace), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Texto do traço vazio (o que acontece ao iniciar).</summary>
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    /// <summary>Unidade do rótulo da média (FPS).</summary>
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public void Clear()
    {
        _samples.Clear();
        InvalidateVisual();
    }

    /// <summary>Acrescenta uma leitura; zero e valores inválidos são "sem leitura" (o motor ainda não mediu um quadro).</summary>
    public void Add(double progress, double value)
    {
        if (!double.IsFinite(value) || value <= 0 || !double.IsFinite(progress)) return;
        progress = Math.Clamp(progress, 0, 1);
        if (_samples.Count > 0 && progress < _samples[^1].X) return; // nova etapa reiniciou o progresso: mantém a ordem
        _samples.Add(new Point(progress, value));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 120 || h < 80) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        const double left = 44, bottom = 20, top = 12;
        double plotW = w - left, plotH = h - top - bottom;

        double peak = _samples.Count > 0 ? _samples.Max(p => p.Y) : 0;
        double step = peak <= 40 ? 10 : peak <= 100 ? 20 : peak <= 250 ? 50 : peak <= 500 ? 100 : peak <= 1000 ? 200 : 500;
        double hi = Math.Max(step * 3, Math.Ceiling(peak * 1.12 / step) * step);
        double X(double p) => left + plotW * p;
        double Y(double v) => top + plotH * (1 - Math.Clamp(v, 0, hi) / hi);

        var grid = Stroke(TrackBrush, 1);
        for (double v = 0; v <= hi + 0.1; v += step)
        {
            double y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(grid, new Point(left, y), new Point(w, y));
            var t = Text($"{v:0}", 11, FaintBrush);
            dc.DrawText(t, new Point(left - 10 - t.Width, y - t.Height / 2));
        }
        var start = Text("início", 11, FaintBrush);
        var end = Text("fim", 11, FaintBrush);
        dc.DrawText(start, new Point(left, h - start.Height + 2));
        dc.DrawText(end, new Point(w - end.Width, h - end.Height + 2));

        if (_samples.Count < 2)
        {
            var t = Text(EmptyText, 12.5, MutedBrush, readout: false);
            t.MaxTextWidth = Math.Max(40, plotW - 40);
            t.TextAlignment = TextAlignment.Center;
            dc.DrawText(t, new Point(left + 20, top + plotH / 2 - t.Height / 2));
            return;
        }

        var points = _samples.Select(s => new Point(X(s.X), Y(s.Y))).ToList();
        double floor = top + plotH;
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, floor), true, true);
            ctx.PolyLineTo(points, true, false);
            ctx.LineTo(new Point(points[^1].X, floor), true, false);
        }
        area.Freeze();
        dc.DrawGeometry(Alpha(ChannelBrush, 0x30), null, area);
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(ChannelBrush, 1.8) { LineJoin = PenLineJoin.Round }, line);

        // Média dos pontos à vista, rotulada na própria linha (a média exata do teste fica na régua do resultado)
        double mean = _samples.Average(s => s.Y);
        double my = Math.Round(Y(mean)) + 0.5;
        dc.DrawLine(Stroke(MutedBrush, 1, new DashStyle(new double[] { 4, 3 }, 0)), new Point(left, my), new Point(points[^1].X, my));
        var label = Text($"média {mean:0} {Unit}".TrimEnd(), 11.5, MutedBrush);
        double lx = Math.Max(left + 6, Math.Min(points[^1].X, w) - label.Width - 6);
        dc.DrawRectangle(SurfaceBrush, null, new Rect(lx - 4, my - label.Height - 2, label.Width + 8, label.Height));
        dc.DrawText(label, new Point(lx, my - label.Height - 2));

        dc.DrawEllipse(SurfaceBrush, Stroke(ChannelBrush, 2), points[^1], 4, 4);
    }
}
