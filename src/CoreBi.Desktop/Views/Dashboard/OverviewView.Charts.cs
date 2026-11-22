using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Storage;

namespace CoreBi.Desktop.Views;

public partial class OverviewView
{
    private sealed record DashPlot(List<DashSample> Data, Func<DashSample, float?> Cpu, Func<DashSample, float?> Gpu, string Unit,
                                   double Left, double Top, double PlotW, double PlotH, Func<int, double> X, Func<double, double> Y);

    private static readonly DependencyProperty RingRevealProperty = DependencyProperty.Register(
        "RingReveal", typeof(double), typeof(OverviewView), new PropertyMetadata(1.0, (d, _) => ((OverviewView)d).DrawRings()));

    private DashPlot? _plot;
    private double? _hoverX;
    private Line? _hoverLine;
    private Ellipse? _hoverCpu, _hoverGpu;
    private Border? _hoverTip;
    private TextBlock? _hoverTime, _hoverCpuText, _hoverGpuText;
    private List<(double Frac, string Channel)> _rings = new();

    private void DashOption_Checked(object sender, RoutedEventArgs e)
    {
        if (!_metricAuto && sender is RadioButton { GroupName: "DashMetric" }) _metricChosen = true;
        if (DashChart != null) DrawChart();
    }

    private void DashChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>
    /// Sem sensor de temperatura o gráfico abre na carga, que sempre tem leitura; quando a temperatura aparece, volta
    /// para ela. Só enquanto a pessoa não escolheu uma aba.
    /// </summary>
    private void PickMetric()
    {
        if (_context == null || _metricChosen || !_context.Sensors.IsOpen || _samples.Count < 3) return;
        bool hasTemp = _samples.Skip(Math.Max(0, _samples.Count - 60)).Any(s => s.CpuT != null || s.GpuT != null);
        string want = hasTemp ? "temp" : "load";
        if (Ui.CheckedTag(DashMetricPanel) == want) return;
        _metricAuto = true;
        try
        {
            foreach (var rb in DashMetricPanel.Children.OfType<RadioButton>())
                if ((string)rb.Tag == want) rb.IsChecked = true;
        }
        finally { _metricAuto = false; }
    }

    internal void DrawChart()
    {
        DashChart.Children.Clear();
        double w = DashChart.ActualWidth, h = DashChart.ActualHeight;
        if (w < 80 || h < 60) return;

        int range = int.Parse(Ui.CheckedTag(DashRangePanel) ?? "300", CultureInfo.InvariantCulture);
        string metric = Ui.CheckedTag(DashMetricPanel) ?? "temp";
        Func<DashSample, float?> cpu = metric switch { "load" => s => s.CpuL, "power" => s => s.CpuW, _ => s => s.CpuT };
        Func<DashSample, float?> gpu = metric switch { "load" => s => s.GpuL, "power" => s => s.GpuW, _ => s => s.GpuT };
        string unit = metric switch { "load" => " %", "power" => " W", _ => " °C" };
        string name = metric switch { "load" => "Carga", "power" => "Consumo", _ => "Temperatura" };
        string span = range >= 60 ? $"{range / 60} min" : $"{range} s";
        var data = _samples.Skip(Math.Max(0, _samples.Count - range)).ToList();

        float? cNow = data.Count > 0 ? cpu(data[^1]) : null;
        float? gNow = data.Count > 0 ? gpu(data[^1]) : null;
        DashChartLabel.Text = $"{name} nos últimos {span}";
        DashChartValue.Text = cNow is float cv ? $"{cv:0.#}{unit}" : "—";
        DashChartValue2.Text = gNow is float gv ? $"{gv:0.#}{unit}" : "—";
        // Sem leitura o mostrador fica no travessão (nunca some: as duas legendas não trocam de lugar) e a dica diz por quê
        DashChartValue.ToolTip = cNow == null && data.Count > 0 && metric != "load" ? "Sem leitura do processador. Veja o aviso na barra lateral." : null;
        DashChartValue2.ToolTip = gNow == null && data.Count > 0 ? "Sem leitura da placa de vídeo." : null;

        var all = data.SelectMany(s => new[] { cpu(s), gpu(s) }).Where(v => v != null).Select(v => (double)v!.Value).ToList();
        double peak = all.Count > 0 ? all.Max() : 0;
        double yMin = metric == "temp" ? 20 : 0;
        double step = metric switch
        {
            "load" => 25,
            "power" => ChartScale.PowerStep(peak),
            _ => 20,
        };
        double yMax = metric switch
        {
            "load" => 100,
            "power" => Math.Max(step * 4, Math.Ceiling(peak * 1.15 / step) * step),
            _ => Math.Max(100, yMin + Math.Ceiling((peak + 5 - yMin) / step) * step),
        };

        const double left = 44, bottom = 22, top = 6;
        double plotW = w - left, plotH = h - top - bottom;
        double X(int i) => left + plotW * (i + range - data.Count) / Math.Max(1, range - 1);
        double Y(double v) => top + plotH * (1 - (Math.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin));

        var line = ThemeManager.Brush("Line");
        var faint = ThemeManager.Brush("Faint");
        var readout = (FontFamily)FindResource("Readout");

        for (double v = yMin; v <= yMax + 0.1; v += step)
        {
            double y = Y(v);
            DashChart.Children.Add(new Line { X1 = left, X2 = w, Y1 = y, Y2 = y, Stroke = line, StrokeThickness = 1, SnapsToDevicePixels = true });
            var lbl = new TextBlock { Text = $"{v:0}{unit}", FontSize = 11, Foreground = faint, FontFamily = readout };
            Canvas.SetLeft(lbl, 0);
            Canvas.SetTop(lbl, y - 8);
            DashChart.Children.Add(lbl);
        }
        for (int k = 0; k <= 4; k++)
        {
            double x = left + plotW * k / 4;
            DashChart.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + plotH, Stroke = line, StrokeThickness = 1, SnapsToDevicePixels = true });
        }

        double half = range / 2.0;
        string mid = half >= 60 ? $"−{(half / 60).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))} min" : $"−{half:0} s";
        string[] xs = { $"−{span}", mid, "agora" };
        for (int k = 0; k < 3; k++)
        {
            var t = new TextBlock { Text = xs[k], FontSize = 11, Foreground = faint, FontFamily = readout };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, Math.Clamp(left + plotW * k / 2 - t.DesiredSize.Width / 2, left, w - t.DesiredSize.Width));
            Canvas.SetTop(t, h - 16);
            DashChart.Children.Add(t);
        }

        _plot = new DashPlot(data, cpu, gpu, unit, left, top, plotW, plotH, X, Y);
        if (data.Count < 2)
        {
            DrawHover();
            return;
        }

        var gpuPts = new List<Point>();
        var cpuPts = new List<Point>();
        for (int i = 0; i < data.Count; i++)
        {
            if (gpu(data[i]) is float g) gpuPts.Add(new Point(X(i), Y(g)));
            if (cpu(data[i]) is float c) cpuPts.Add(new Point(X(i), Y(c)));
        }

        void Area(List<Point> pts, string channel, byte alpha)
        {
            if (pts.Count < 2) return;
            var area = new Polygon { Fill = ThemeManager.Tint(channel, alpha) };
            foreach (var p in pts) area.Points.Add(p);
            area.Points.Add(new Point(pts[^1].X, top + plotH));
            area.Points.Add(new Point(pts[0].X, top + plotH));
            DashChart.Children.Add(area);
        }
        void Trace(List<Point> pts, string channel, double thickness)
        {
            if (pts.Count < 2) return;
            var trace = new Polyline { Stroke = ThemeManager.Brush(channel), StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round };
            foreach (var p in pts) trace.Points.Add(p);
            DashChart.Children.Add(trace);
        }

        Area(gpuPts, "Gpu", ThemeManager.IsLight ? (byte)0x10 : (byte)0x1C);
        Area(cpuPts, "Cpu", ThemeManager.IsLight ? (byte)0x1A : (byte)0x26);
        Trace(gpuPts, "Gpu", 1.8);
        Trace(cpuPts, "Cpu", 2);

        bool liveMotion = _context?.IsLiveMotion() == true;
        void Cursor(Point p, string channel)
        {
            if (liveMotion)
            {
                var pulse = new ScaleTransform(0.45, 0.45);
                var halo = new Ellipse
                {
                    Width = 24,
                    Height = 24,
                    Fill = new SolidColorBrush(ThemeManager.WithAlpha(channel, 0x50)),
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = pulse,
                };
                Canvas.SetLeft(halo, p.X - 12);
                Canvas.SetTop(halo, p.Y - 12);
                DashChart.Children.Add(halo);
                var grow = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                pulse.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                pulse.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                halo.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400)));
            }
            var dot = new Ellipse { Width = 9, Height = 9, Stroke = ThemeManager.Brush(channel), StrokeThickness = 2, Fill = ThemeManager.Brush("Surface") };
            Canvas.SetLeft(dot, p.X - 4.5);
            Canvas.SetTop(dot, p.Y - 4.5);
            DashChart.Children.Add(dot);
        }
        if (gpuPts.Count > 0) Cursor(gpuPts[^1], "Gpu");
        if (cpuPts.Count > 0) Cursor(cpuPts[^1], "Cpu");
        DrawHover();
    }

    private void DashChart_MouseMove(object sender, MouseEventArgs e)
    {
        bool entering = _hoverX == null;
        _hoverX = e.GetPosition(DashChart).X;
        DrawHover();
        if (!entering) return;
        if (_hoverTip is { Visibility: Visibility.Visible } tip)
            tip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void DashChart_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverX = null;
        DrawChart();
    }

    private void DrawHover()
    {
        if (_hoverTip == null) BuildHover();
        foreach (UIElement el in new UIElement[] { _hoverLine!, _hoverCpu!, _hoverGpu!, _hoverTip! })
            if (!DashChart.Children.Contains(el)) DashChart.Children.Add(el);

        void Hide()
        {
            foreach (UIElement el in new UIElement[] { _hoverLine!, _hoverCpu!, _hoverGpu!, _hoverTip! })
                el.Visibility = Visibility.Collapsed;
        }
        if (_hoverX is not double mx || _plot is not { Data.Count: > 0 } plot || mx < plot.Left) { Hide(); return; }

        int nearest = 0;
        double best = double.MaxValue;
        for (int k = 0; k < plot.Data.Count; k++)
        {
            double d = Math.Abs(plot.X(k) - mx);
            if (d < best) { best = d; nearest = k; }
        }
        if (best > Math.Max(12, plot.PlotW / Math.Max(1, plot.Data.Count))) { Hide(); return; }

        var sample = plot.Data[nearest];
        double px = plot.X(nearest);
        float? c = plot.Cpu(sample), g = plot.Gpu(sample);
        string Fmt(float? v) => v is float x ? $"{x:0.#}{plot.Unit}" : "sem leitura";

        _hoverLine!.X1 = _hoverLine.X2 = Math.Round(px) + 0.5;
        _hoverLine.Y1 = plot.Top;
        _hoverLine.Y2 = plot.Top + plot.PlotH;
        _hoverLine.Visibility = Visibility.Visible;
        void Dot(Ellipse dot, float? v)
        {
            dot.Visibility = v is null ? Visibility.Collapsed : Visibility.Visible;
            if (v is float y)
            {
                Canvas.SetLeft(dot, px - dot.Width / 2);
                Canvas.SetTop(dot, plot.Y(y) - dot.Height / 2);
            }
        }
        Dot(_hoverGpu!, g);
        Dot(_hoverCpu!, c);

        int ago = (int)Math.Round((DateTime.Now - sample.At).TotalSeconds);
        _hoverTime!.Text = ago <= 1 ? $"{sample.At:HH:mm:ss} · agora" : $"{sample.At:HH:mm:ss} · {ChartScale.RelativeAgo(ago)}";
        _hoverCpuText!.Text = Fmt(c);
        _hoverGpuText!.Text = Fmt(g);

        var tip = _hoverTip!;
        tip.Visibility = Visibility.Visible;
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tw = tip.DesiredSize.Width, th = tip.DesiredSize.Height;
        double tx = px + 14 + tw > DashChart.ActualWidth ? px - 14 - tw : px + 14;
        double anchor = new[] { c, g }.Where(v => v != null).Select(v => plot.Y(v!.Value)).DefaultIfEmpty(plot.Top + plot.PlotH / 2).Min();
        double ty = Math.Clamp(anchor - th / 2, plot.Top, Math.Max(plot.Top, plot.Top + plot.PlotH - th));
        Canvas.SetLeft(tip, tx);
        Canvas.SetTop(tip, ty);
    }

    private void BuildHover()
    {
        _hoverLine = new Line { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false, SnapsToDevicePixels = true };
        _hoverLine.SetResourceReference(Shape.StrokeProperty, "Faint");
        static Ellipse Dot(string channel)
        {
            var dot = new Ellipse { Width = 11, Height = 11, StrokeThickness = 2.5, IsHitTestVisible = false };
            dot.SetResourceReference(Shape.StrokeProperty, channel);
            dot.SetResourceReference(Shape.FillProperty, "Surface");
            return dot;
        }
        _hoverCpu = Dot("Cpu");
        _hoverGpu = Dot("Gpu");

        var readout = (FontFamily)FindResource("Readout");
        TextBlock Value()
        {
            var t = new TextBlock { FontFamily = readout, FontStretch = FontStretches.SemiCondensed, FontSize = 15, Margin = new Thickness(16, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            return t;
        }
        static Grid Row(string channel, string name, TextBlock value)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var swatch = new Border { Width = 10, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            swatch.SetResourceReference(Border.BackgroundProperty, channel);
            var label = new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, label } };
            Grid.SetColumn(value, 1);
            row.Children.Add(left);
            row.Children.Add(value);
            return row;
        }
        _hoverTime = new TextBlock { FontSize = 11.5 };
        _hoverTime.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        _hoverTime.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        _hoverCpuText = Value();
        _hoverGpuText = Value();
        _hoverTip = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            IsHitTestVisible = false,
            Child = new StackPanel { Children = { _hoverTime, Row("Cpu", "Processador", _hoverCpuText), Row("Gpu", "Placa de vídeo", _hoverGpuText) } },
        };
        _hoverTip.SetResourceReference(Border.BackgroundProperty, "Raised");
        _hoverTip.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        Panel.SetZIndex(_hoverTip, 10);
    }

    internal void RevealRings()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        BeginAnimation(RingRevealProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void UpdateRings()
    {
        if (_context == null) return;
        static string Gb(double bytes) => $"{bytes / 1073741824.0:0.0} GB";
        static string Pct(double fraction) => $"{fraction * 100:0} %"; // o "P0" do pt-BR cola o sinal no número
        var mem = MemoryInfo.ReadPhysicalUsage();
        double ram = mem is { Total: > 0 } u ? (double)u.Used / u.Total : 0;
        Brush memoryBrush = ThemeManager.Brush("Memory");
        Brush gpuBrush = ThemeManager.Brush("Gpu");
        Brush storageBrush = ThemeManager.Brush("Storage");

        var rings = new List<(double Frac, string Channel, DashRingItem Item)>();
        if (mem is { Total: > 0 } mu)
            rings.Add((ram, "Memory", new(memoryBrush, "RAM", Pct(ram), $"{Gb(mu.Used)} de {mu.Total / 1073741824.0:0} GB · {Gb(mu.Available)} livres", "PageSystem:Memória RAM")));
        if (_context.PrimaryGpu is { } gpu && GpuMonitor.ReadLive(gpu, _context.Sensors.Snapshot()) is { VramUsedGb: double vu, VramTotalGb: > 0 and double vt })
        {
            double vram = vu / vt;
            rings.Add((vram, "Gpu", new(gpuBrush, "VRAM", Pct(vram), $"{vu:0.0} GB de {vt:0} GB · {Math.Max(0, vt - vu):0.0} GB livres", "PageGpu")));
        }
        if (_context.Drives.FirstOrDefault(d => d.Letter.StartsWith("C", StringComparison.OrdinalIgnoreCase)) is { } c0)
            rings.Add((c0.UsedFraction, "Storage", new(storageBrush, $"Disco {c0.Letter}", Pct(c0.UsedFraction),
                $"{DriveCard.Fmt(c0.Total - c0.Free)} de {DriveCard.Fmt(c0.Total)} · {DriveCard.Fmt(c0.Free)} livres", "PageDisks")));

        _rings = rings.Select(r => (r.Frac, r.Channel)).ToList();
        DrawRings();
        DashRingLegend.ItemsSource = rings.Select(r => r.Item).ToList();
    }

    private void DrawRings()
    {
        DashRings.Children.Clear();
        double reveal = (double)GetValue(RingRevealProperty);
        const double thickness = 9, step = 17;
        double cx = DashRings.Width / 2, cy = DashRings.Height / 2;
        for (int i = 0; i < _rings.Count; i++)
        {
            double r = cx - thickness / 2 - 1 - i * step;
            DashRings.Children.Add(new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Stroke = ThemeManager.Brush("Line"),
                StrokeThickness = thickness,
                Margin = new Thickness(cx - r, cy - r, 0, 0),
            });
            DashRings.Children.Add(new Path
            {
                Data = RingArc(_rings[i].Frac * reveal, cx, cy, r),
                Stroke = ThemeManager.Brush(_rings[i].Channel),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
    }

    internal static Geometry RingArc(double frac, double cx, double cy, double r)
    {
        frac = Math.Clamp(frac, 0, 0.9999);
        if (frac <= 0.001) return Geometry.Empty;
        double a = frac * 2 * Math.PI;
        var fig = new PathFigure { StartPoint = new Point(cx, cy - r) };
        fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a)), new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        return g;
    }
}
