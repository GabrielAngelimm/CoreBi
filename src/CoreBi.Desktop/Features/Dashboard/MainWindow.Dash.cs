using LibreHardwareMonitor.Hardware;
using CoreBi.Engine.Games;
using CoreBi.Engine.Monitoring.Diagnostics;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CoreBi.Desktop;

/// <summary>
/// Visão geral: osciloscópio de dois canais (CPU × GPU), uso ao vivo do processador e da placa de vídeo, anéis de
/// ocupação (RAM, VRAM e disco), desempenho, lista de verificação da saúde do PC e eventos recentes.
/// </summary>
public partial class MainWindow
{
    /// <summary>Cartão de uso ao vivo: Hero é a carga, com medidor segmentado; Stats só traz leituras que existem para o componente.</summary>
    public sealed record DashComponent(string Icon, string Name, string Model, string Target, Brush Channel,
                                       string Hero, string HeroUnit, double Fraction, IReadOnlyList<KeyValuePair<string, string>> Stats);
    public sealed record DashEvent(string Icon, string Title, string Detail, string Value, DateTime At)
    {
        public string When => At.Date == DateTime.Today ? $"hoje {At:HH:mm}" : $"{At:dd/MM HH:mm}";
    }
    public sealed record DashRingItem(Brush Swatch, string Label, string Value, string Detail, string Target);
    public sealed record DashCheck(string Title, string Detail, bool Ok);

    internal readonly record struct DashSample(DateTime At, float? CpuT, float? GpuT, float? CpuL, float? GpuL, float? CpuW, float? GpuW);

    internal const int DashMax = 900; // 15 min a 1 amostra/s
    /// <summary>Altura de uma linha da atividade recente: a lista mostra quantas couberem no cartão.</summary>
    public const double DashEventHeight = 56;
    /// <summary>Máximo de linhas no cartão (o resto vai para "Ver histórico") e de linhas no histórico.</summary>
    internal const int DashEventsMax = 5, DashEventsKept = 50;
    internal readonly List<DashSample> _dash = new();
    internal readonly List<DashEvent> _events = new();
    internal List<DashCheck> _health = new();
    internal DateTime _healthAt = DateTime.MinValue;

    // ---------------- coleta (a cada segundo, mesmo com a página fechada) ----------------

    internal float? MainGpuLoad() => _gpus.FirstOrDefault()?.WindowsLoad;

    internal float? CpuClockAvg()
    {
        var c = _monitor.Rows.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock &&
                                         r.Name.StartsWith("Core #") && r.RawValue > 0).Select(r => r.RawValue!.Value).ToList();
        return c.Count > 0 ? c.Average() : null;
    }

    internal void CollectDashSample()
    {
        _dash.Add(new DashSample(DateTime.Now, _monitor.CpuTemperature, MainGpuTemp(), _monitor.CpuLoad, MainGpuLoad(), _monitor.CpuPower, MainGpuPower()));
        if (_dash.Count > DashMax) _dash.RemoveAt(0);
        if (PageDash.IsVisible) UpdateDashboard(full: false);
    }

    // ---------------- eventos recentes ----------------

    internal void AddEvent(string icon, string title, string detail, string value)
    {
        _events.Insert(0, new DashEvent(icon, title, detail, value, DateTime.Now));
        if (_events.Count > 30) _events.RemoveAt(_events.Count - 1);
        if (PageDash.IsVisible) ShowEvents();
    }

    /// <summary>Eventos já registrados antes desta sessão (resultados de benchmark e verificações salvos).</summary>
    internal IEnumerable<DashEvent> SavedEvents()
    {
        foreach (var r in _settings.BenchRuns)
        {
            if (r.Multi is double m) yield return new DashEvent("", "Benchmark", "Render multi-core", $"{m:N0} pts", r.Date);
            else if (r.Single is double s) yield return new DashEvent("", "Benchmark", "Render single-core", $"{s:N0} pts", r.Date);
        }
        foreach (var r in _settings.BenchRecords)
        {
            var (icon, title) = r.Test switch
            {
                "gpu" => ("", "Cena de jogo (GPU)"),
                "sim" => ("", "Simulação de jogo"),
                "mem" => ("", "Memória e carregamento"),
                "disk" => ("", "Benchmark de disco"),
                _ => ("", r.Test)
            };
            string first = r.Summary.Split('·')[0].Trim();
            string rest = r.Summary.Contains('·') ? r.Summary[(r.Summary.IndexOf('·') + 1)..].Trim() : "";
            yield return new DashEvent(icon, title, rest, first, r.Date);
        }
    }

    internal void ShowEvents()
    {
        // o espaço vem do host (que não pede altura), então a lista nunca estica a página; o resto fica em "Ver histórico"
        double room = ((FrameworkElement)DashEvents.Parent).ActualHeight;
        int slots = room > 0 ? Math.Clamp((int)(room / DashEventHeight), 1, DashEventsMax) : 3;
        var all = _events.Concat(SavedEvents()).OrderByDescending(e => e.At).ToList();
        var list = all.Take(slots).ToList();
        if (DashEvents.ItemsSource is IList<DashEvent> shown && shown.SequenceEqual(list)) return;
        DashEvents.ItemsSource = list;
        DashEventsAll.ItemsSource = all.Take(DashEventsKept).ToList();
        DashEventsMore.Visibility = all.Count > list.Count ? Visibility.Visible : Visibility.Collapsed;
        DashEventsEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- atualização ----------------

    internal void DashOption_Checked(object sender, RoutedEventArgs e) { if (DashChart != null) DrawDashChart(); }
    internal void DashChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawDashChart();
    internal void DashEvents_SizeChanged(object sender, SizeChangedEventArgs e) { if (e.HeightChanged) ShowEvents(); }
    internal void DashBench_Click(object sender, RoutedEventArgs e) => NavBench.IsChecked = true;
    internal void DashSensors_Click(object sender, RoutedEventArgs e) => NavSensors.IsChecked = true;

    /// <summary>Setas e linhas da Visão geral. Tag = página ou "página:categoria" (categoria do inventário em Sistema).</summary>
    internal void DashGo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string target }) return;
        var parts = target.Split(':', 2);
        var nav = new RadioButton[] { NavSensors, NavGpu, NavSystem, NavDisks }.FirstOrDefault(n => (string)n.Tag == parts[0]);
        if (nav != null) nav.IsChecked = true;
        if (parts.Length == 2 && InfoCategories.ItemsSource is IEnumerable<InfoNode> cats && cats.FirstOrDefault(c => c.Header == parts[1]) is { } node)
        {
            InfoCategories.SelectedItem = node;
            InfoCategories.ScrollIntoView(node);
        }
    }

    internal void UpdateDashboard(bool full = true)
    {
        // Com o mouse sobre o gráfico ele fica parado na leitura que a pessoa está olhando; volta a andar ao sair.
        if (_dashHoverX == null) DrawDashChart();
        UpdateDashComponents();
        UpdateDashRings();
        if (full)
        {
            UpdateDashScores();
            ShowEvents();
        }
        if (full || (DateTime.Now - _healthAt).TotalSeconds > 60) UpdateDashHealth();
    }

    internal static string F(float? v, string fmt, string unit) => v is float x ? x.ToString(fmt) + unit : "—";

    internal void UpdateDashComponents()
    {
        static KeyValuePair<string, string> Stat(string label, string value) => new(label, value);

        var cpuLoad = _monitor.CpuLoad;
        var rows = new List<DashComponent>
        {
            new("", "Processador", GameCatalog.ShortCpuName(_cpuName), "PageSystem:Processador", Palette("Accent"),
                F(cpuLoad, "0", " %"), "em uso", (cpuLoad ?? 0) / 100.0,
                new[] { Stat("Temp.", F(_monitor.CpuTemperature, "0", " °C")), Stat("Clock", F(CpuClockAvg(), "0", " MHz")),
                        Stat("Consumo", F(_monitor.CpuPower, "0", " W")) }),
        };
        if (_gpus.Count > 0)
        {
            var gpu = _gpus[0];
            uint? clk = gpu.NvmlHandle != IntPtr.Zero ? Nvml.Clock(gpu.NvmlHandle, Nvml.ClockGraphics) : null;
            var gpuLoad = MainGpuLoad();
            rows.Add(new("", "Placa de vídeo", GameCatalog.ShortGpuName(gpu.Name), "PageGpu", Palette("Copper"),
                         F(gpuLoad, "0", " %"), "em uso", (gpuLoad ?? 0) / 100.0,
                         new[] { Stat("Temp.", F(MainGpuTemp(), "0", " °C")), Stat("Clock", clk is uint c ? $"{c} MHz" : "—"),
                                 Stat("Consumo", F(MainGpuPower(), "0", " W")) }));
        }
        DashComponents.ItemsSource = rows;
    }

    internal void UpdateDashScores()
    {
        var runs = _settings.BenchRuns.Where(r => r.Multi != null).ToList();
        if (runs.Count > 0)
        {
            DashScore.Text = $"{runs[0].Multi:N0}";
            DashScoreSub.Text = $"Render multi-core, medido em {runs[0].Date:dd/MM 'às' HH:mm}";
            if (runs.Count > 1)
            {
                double d = runs[0].Multi!.Value - runs[1].Multi!.Value;
                DashScoreDelta.Text = d >= 0 ? $"+{d:N0} desde o anterior" : $"−{-d:N0} desde o anterior";
            }
            else DashScoreDelta.Text = "";
        }
        else { DashScore.Text = "—"; DashScoreDelta.Text = ""; DashScoreSub.Text = "Rode o benchmark para ter uma nota de referência."; }

        string Last(string test) => _settings.BenchRecords.FirstOrDefault(r => r.Test == test)?.Summary.Split('·')[0].Trim() ?? "—";
        var single = _settings.BenchRuns.FirstOrDefault(r => r.Single != null)?.Single;
        DashScores.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("Render single-core", single is double s ? $"{s:N0} pts" : "—"),
            new KeyValuePair<string, string>("Cena de jogo (GPU)", Last("gpu")),
            new KeyValuePair<string, string>("Simulação de jogo", Last("sim")),
        };
        if (_cpuName.Length > 0 && _gpus.Count > 0)
            DashMachine.Text = $"Leituras ao vivo de {Environment.MachineName}: {GameCatalog.ShortCpuName(_cpuName)} e {GameCatalog.ShortGpuName(_gpus[0].Name)}.";
    }

    // ---------------- osciloscópio (CPU × GPU) ----------------

    /// <summary>
    /// Gráfico em retícula, como a tela de um instrumento: canal A (processador, verde-água, com área)
    /// e canal B (placa de vídeo, cobre). O valor atual fica nas leituras grandes acima, não no gráfico.
    /// </summary>
    internal void DrawDashChart()
    {
        DashChart.Children.Clear();
        double w = DashChart.ActualWidth, h = DashChart.ActualHeight;
        if (w < 80 || h < 60) return;

        int range = int.Parse(CheckedTag(DashRangePanel) ?? "300");
        string metric = CheckedTag(DashMetricPanel) ?? "temp";
        Func<DashSample, float?> cpu = metric switch { "load" => s => s.CpuL, "power" => s => s.CpuW, _ => s => s.CpuT };
        Func<DashSample, float?> gpu = metric switch { "load" => s => s.GpuL, "power" => s => s.GpuW, _ => s => s.GpuT };
        string unit = metric switch { "load" => " %", "power" => " W", _ => " °C" };
        string name = metric switch { "load" => "Carga", "power" => "Consumo", _ => "Temperatura" };
        string span = range >= 60 ? $"{range / 60} min" : $"{range} s";
        var data = _dash.Skip(Math.Max(0, _dash.Count - range)).ToList();

        float? cNow = data.Count > 0 ? cpu(data[^1]) : null, gNow = data.Count > 0 ? gpu(data[^1]) : null;
        DashChartLabel.Text = $"{name} nos últimos {span}";
        DashChartValue.Text = cNow is float cv ? $"{cv:0.#}{unit}" : "—";
        DashChartValue2.Text = gNow is float gv ? $"{gv:0.#}{unit}" : "—";
        var firstC = data.Select(cpu).FirstOrDefault(v => v != null);
        DashChartDelta.Text = cNow is float a && firstC is float b
            ? $"Processador {(a - b >= 0 ? "+" : "−")}{Math.Abs(a - b):0.#}{unit} no período"
            : cNow == null && metric != "load" ? "Sem leitura do processador. Veja o aviso na barra lateral." : "Coletando as primeiras amostras…";

        var all = data.SelectMany(s => new[] { cpu(s), gpu(s) }).Where(v => v != null).Select(v => (double)v!.Value).ToList();
        double yMin = metric == "temp" ? 20 : 0;
        double yMax = metric switch
        {
            "load" => 100,
            "power" => Math.Max(50, all.Count > 0 ? Math.Ceiling(all.Max() * 1.2 / 10) * 10 : 50),
            _ => Math.Max(100, all.Count > 0 ? Math.Ceiling((all.Max() + 5) / 10) * 10 : 100)
        };
        const double left = 44, bottom = 22, top = 6;
        double plotW = w - left, plotH = h - top - bottom;
        double X(int i) => left + plotW * (i + range - data.Count) / Math.Max(1, range - 1);
        double Y(double v) => top + plotH * (1 - (Math.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin));

        var line = Palette("Line");
        var faint = Palette("Faint");
        var readout = (FontFamily)FindResource("Readout");

        // Retícula: 4 divisões em cada eixo, com a borda do quadro um tom acima
        for (int k = 0; k <= 4; k++)
        {
            double v = yMin + (yMax - yMin) * k / 4, y = Y(v);
            DashChart.Children.Add(new Line { X1 = left, X2 = w, Y1 = y, Y2 = y, Stroke = line, StrokeThickness = 1, SnapsToDevicePixels = true });
            if (k % 2 == 0)
            {
                var lbl = new TextBlock { Text = $"{v:0}{unit.Trim()}", FontSize = 11, Foreground = faint, FontFamily = readout };
                Canvas.SetLeft(lbl, 0); Canvas.SetTop(lbl, y - 8);
                DashChart.Children.Add(lbl);
            }
        }
        for (int k = 0; k <= 4; k++)
        {
            double x = left + plotW * k / 4;
            DashChart.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + plotH, Stroke = line, StrokeThickness = 1, SnapsToDevicePixels = true });
        }
        string[] xs = { $"−{span}", range >= 120 ? $"−{range / 120} min" : $"−{range / 2} s", "agora" };
        for (int k = 0; k < 3; k++)
        {
            var t = new TextBlock { Text = xs[k], FontSize = 11, Foreground = faint, FontFamily = readout };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, Math.Clamp(left + plotW * k / 2 - t.DesiredSize.Width / 2, left, w - t.DesiredSize.Width));
            Canvas.SetTop(t, h - 16);
            DashChart.Children.Add(t);
        }

        _dashPlot = new DashPlot(data, cpu, gpu, unit, left, top, plotW, plotH, X, Y);
        if (data.Count < 2) { DrawDashHover(); return; }

        // Canal B: placa de vídeo
        var copper = Palette("Copper");
        var gLine = new Polyline { Stroke = copper, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        for (int i = 0; i < data.Count; i++) if (gpu(data[i]) is float g) gLine.Points.Add(new Point(X(i), Y(g)));
        DashChart.Children.Add(gLine);

        // Canal A: processador, com área em degradê do próprio tom
        var accent = Palette("Accent");
        var pts = new List<Point>();
        for (int i = 0; i < data.Count; i++) if (cpu(data[i]) is float c) pts.Add(new Point(X(i), Y(c)));
        if (pts.Count < 2) return;
        var col = accent.Color;
        var area = new Polygon
        {
            Fill = new LinearGradientBrush(Color.FromArgb(0x44, col.R, col.G, col.B), Color.FromArgb(0x00, col.R, col.G, col.B), 90)
        };
        foreach (var p in pts) area.Points.Add(p);
        area.Points.Add(new Point(pts[^1].X, top + plotH));
        area.Points.Add(new Point(pts[0].X, top + plotH));
        DashChart.Children.Add(area);
        var cLine = new Polyline { Stroke = accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        foreach (var p in pts) cLine.Points.Add(p);
        DashChart.Children.Add(cLine);

        // Ponto de leitura atual em cada canal
        void Cursor(Point p, Brush stroke)
        {
            var dot = new Ellipse { Width = 9, Height = 9, Stroke = stroke, StrokeThickness = 2, Fill = Palette("Surface") };
            Canvas.SetLeft(dot, p.X - 4.5); Canvas.SetTop(dot, p.Y - 4.5);
            DashChart.Children.Add(dot);
        }
        if (gLine.Points.Count > 0) Cursor(gLine.Points[^1], copper);
        Cursor(pts[^1], accent);
        DrawDashHover();
    }

    // ---------------- leitura sob o mouse ----------------

    /// <summary>Mapeamento do último desenho: amostras, métrica e conversão amostra/valor → pixel.</summary>
    internal sealed record DashPlot(List<DashSample> Data, Func<DashSample, float?> Cpu, Func<DashSample, float?> Gpu, string Unit,
                                    double Left, double Top, double PlotW, double PlotH, Func<int, double> X, Func<double, double> Y);

    internal DashPlot? _dashPlot;
    private double? _dashHoverX;
    private Line? _hoverLine;
    private Ellipse? _hoverCpu, _hoverGpu;
    private Border? _hoverTip;
    private TextBlock? _hoverTime, _hoverCpuText, _hoverGpuText;

    internal void DashChart_MouseMove(object sender, MouseEventArgs e)
    {
        bool entering = _dashHoverX == null;
        _dashHoverX = e.GetPosition(DashChart).X;
        DrawDashHover();
        if (!entering) return;
        DashChartDelta.Text = "Pausado enquanto o mouse está sobre o gráfico";
        if (_hoverTip is { Visibility: Visibility.Visible } tip)
            tip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    internal void DashChart_MouseLeave(object sender, MouseEventArgs e)
    {
        _dashHoverX = null;
        DrawDashChart(); // retoma com as amostras que chegaram durante a pausa
    }

    /// <summary>
    /// Cursor do osciloscópio: linha vertical na amostra mais próxima, um ponto em cada canal e um quadro discreto com a
    /// hora e o valor exato de cada canal. Os elementos são reaproveitados porque o gráfico é redesenhado a cada segundo.
    /// </summary>
    internal void DrawDashHover()
    {
        if (_hoverTip == null) BuildDashHover();
        foreach (UIElement el in new UIElement[] { _hoverLine!, _hoverCpu!, _hoverGpu!, _hoverTip! })
            if (!DashChart.Children.Contains(el)) DashChart.Children.Add(el);

        void Hide() { foreach (UIElement el in new UIElement[] { _hoverLine!, _hoverCpu!, _hoverGpu!, _hoverTip! }) el.Visibility = Visibility.Collapsed; }
        if (_dashHoverX is not double mx || _dashPlot is not { Data.Count: > 0 } plot || mx < plot.Left) { Hide(); return; }

        // Amostra mais próxima do mouse (a última fica sempre no "agora" da direita)
        int nearest = 0; double best = double.MaxValue;
        for (int k = 0; k < plot.Data.Count; k++)
        {
            double d = Math.Abs(plot.X(k) - mx);
            if (d < best) { best = d; nearest = k; }
        }
        if (best > Math.Max(12, plot.PlotW / Math.Max(1, plot.Data.Count))) { Hide(); return; }

        var s = plot.Data[nearest];
        double px = plot.X(nearest);
        float? c = plot.Cpu(s), g = plot.Gpu(s);
        string Fmt(float? v) => v is float x ? $"{x:0.#}{plot.Unit}" : "sem leitura";

        _hoverLine!.X1 = _hoverLine.X2 = Math.Round(px) + 0.5;
        _hoverLine.Y1 = plot.Top; _hoverLine.Y2 = plot.Top + plot.PlotH;
        _hoverLine.Visibility = Visibility.Visible;
        void Dot(Ellipse dot, float? v)
        {
            dot.Visibility = v is null ? Visibility.Collapsed : Visibility.Visible;
            if (v is float y) { Canvas.SetLeft(dot, px - dot.Width / 2); Canvas.SetTop(dot, plot.Y(y) - dot.Height / 2); }
        }
        Dot(_hoverGpu!, g);
        Dot(_hoverCpu!, c);

        int ago = (int)Math.Round((DateTime.Now - s.At).TotalSeconds);
        _hoverTime!.Text = ago <= 1 ? $"{s.At:HH:mm:ss} · agora" : $"{s.At:HH:mm:ss} · há {(ago >= 60 ? $"{ago / 60} min {ago % 60:00} s" : $"{ago} s")}";
        _hoverCpuText!.Text = Fmt(c);
        _hoverGpuText!.Text = Fmt(g);

        var tip = _hoverTip!;
        tip.Visibility = Visibility.Visible;
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tw = tip.DesiredSize.Width, th = tip.DesiredSize.Height;
        double tx = px + 14 + tw > DashChart.ActualWidth ? px - 14 - tw : px + 14;
        double anchor = new[] { c, g }.Where(v => v != null).Select(v => plot.Y(v!.Value)).DefaultIfEmpty(plot.Top + plot.PlotH / 2).Min();
        double ty = Math.Clamp(anchor - th / 2, plot.Top, Math.Max(plot.Top, plot.Top + plot.PlotH - th));
        Canvas.SetLeft(tip, tx); Canvas.SetTop(tip, ty);
    }

    private void BuildDashHover()
    {
        _hoverLine = new Line { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false, SnapsToDevicePixels = true };
        _hoverLine.SetResourceReference(Shape.StrokeProperty, "Faint");
        Ellipse Dot(string channel)
        {
            var dot = new Ellipse { Width = 11, Height = 11, StrokeThickness = 2.5, IsHitTestVisible = false };
            dot.SetResourceReference(Shape.StrokeProperty, channel);
            dot.SetResourceReference(Shape.FillProperty, "Surface");
            return dot;
        }
        _hoverCpu = Dot("Accent");
        _hoverGpu = Dot("Copper");

        var readout = (FontFamily)FindResource("Readout");
        TextBlock Value()
        {
            var t = new TextBlock { FontFamily = readout, FontStretch = FontStretches.SemiCondensed, FontSize = 15, Margin = new Thickness(18, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            return t;
        }
        Grid Row(string channel, string name, TextBlock value)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var swatch = new Border { Width = 10, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            swatch.SetResourceReference(Border.BackgroundProperty, channel);
            var label = new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, label } };
            Grid.SetColumn(value, 1);
            row.Children.Add(left); row.Children.Add(value);
            return row;
        }
        _hoverTime = new TextBlock { FontSize = 11.5 };
        _hoverTime.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        _hoverTime.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        _hoverCpuText = Value();
        _hoverGpuText = Value();
        _hoverTip = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 8, 11, 9),
            IsHitTestVisible = false,
            Child = new StackPanel { Children = { _hoverTime, Row("Accent", "Processador", _hoverCpuText), Row("Copper", "Placa de vídeo", _hoverGpuText) } }
        };
        _hoverTip.SetResourceReference(Border.BackgroundProperty, "Raised");
        _hoverTip.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        Panel.SetZIndex(_hoverTip, 10);
    }

    // ---------------- anéis concêntricos (ocupação) ----------------

    /// <summary>
    /// Mostrador de anéis: RAM no verde-água, VRAM no cobre e disco do sistema no aço. Memória e disco não têm
    /// temperatura nem clock, então o mostrador fala só de ocupação: fração usada, quanto está em uso e o que sobra.
    /// </summary>
    internal void UpdateDashRings()
    {
        static string Gb(double bytes) => $"{bytes / 1073741824.0:0.0} GB";
        var (total, avail) = MemoryInfo.MemoryStatus();
        double ram = total > 0 ? (double)(total - avail) / total : 0;
        Brush accent = Palette("Accent"), copper = Palette("Copper"), steel = Palette("Steel");

        var rings = new List<(double Frac, DashRingItem Item)>();
        if (total > 0) rings.Add((ram, new(accent, "RAM", $"{ram:P0}", $"{Gb(total - avail)} de {total / 1073741824.0:0} GB · {Gb(avail)} livres", "PageSystem:Memória RAM")));
        if (_gpus.Count > 0 && _gpus[0].NvmlHandle != IntPtr.Zero && Nvml.MemoryInfo(_gpus[0].NvmlHandle) is Nvml.Memory m && m.Total > 0)
        {
            double vram = (double)m.Used / m.Total;
            rings.Add((vram, new(copper, "VRAM", $"{vram:P0}", $"{Gb(m.Used)} de {m.Total / 1073741824.0:0} GB · placa de vídeo", "PageGpu")));
        }
        if (_drives.FirstOrDefault(d => d.Letter.StartsWith("C", StringComparison.OrdinalIgnoreCase)) is { } c0)
            rings.Add((c0.UsedFraction, new(steel, $"Disco {c0.Letter}", c0.UsedPercent, $"{DriveCard.Fmt(c0.Free)} livres de {DriveCard.Fmt(c0.Total)}", "PageDisks")));

        DashRings.Children.Clear();
        const double thickness = 9, step = 17;
        double cx = DashRings.Width / 2, cy = DashRings.Height / 2;
        for (int i = 0; i < rings.Count; i++)
        {
            double r = cx - thickness / 2 - 1 - i * step;
            DashRings.Children.Add(new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Stroke = Palette("Line"),
                StrokeThickness = thickness,
                Margin = new Thickness(cx - r, cy - r, 0, 0)
            });
            DashRings.Children.Add(new Path
            {
                Data = RingArc(rings[i].Frac, cx, cy, r),
                Stroke = rings[i].Item.Swatch,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }
        var center = new TextBlock
        {
            Text = total > 0 ? $"{ram:P0}" : "—",
            FontFamily = (FontFamily)FindResource("Readout"),
            FontStretch = FontStretches.SemiCondensed,
            FontSize = 21,
            Foreground = Palette("Fg"),
            ToolTip = "RAM em uso"
        };
        center.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(center, cx - center.DesiredSize.Width / 2); Canvas.SetTop(center, cy - center.DesiredSize.Height / 2);
        DashRings.Children.Add(center);

        DashRingLegend.ItemsSource = rings.Select(r => r.Item).ToList();
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

    // ---------------- saúde do PC ----------------

    internal void UpdateDashHealth()
    {
        _healthAt = DateTime.Now;
        DashCheck Item(bool ok, string title, string okText, string badText) => new(title, ok ? okText : badText, ok);

        var list = new List<DashCheck>();
        if (_monitor.IsOpen) // enquanto detecta o hardware, ainda não dá para saber
        {
            var sensors = _monitor.DiagnoseCpuSensors();
            list.Add(Item(sensors == SensorMonitor.CpuSensorProblem.None, "Sensores da CPU",
                          "Temperatura e consumo disponíveis.", SensorMonitor.Explain(sensors).Split('.')[0] + "."));
        }

        var errs = HardwareEventReader.ErrorsSince(DateTime.Now.AddDays(-7));
        list.Add(Item(errs.Error == null && errs.Total == 0, "Sem erros de hardware",
                      "Nenhum WHEA ou reinício do driver de vídeo em 7 dias.",
                      errs.Error != null ? "Não foi possível ler o registro do Windows."
                                         : $"{errs.Whea} WHEA · {errs.GpuResets} reinícios do driver em 7 dias."));

        _health = list;
        DashHealth.ItemsSource = _health;

        // Selo no título: resume a lista sem precisar ler item por item
        int alerts = list.Count(c => !c.Ok);
        DashHealthBadge.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DashHealthBadgeText.Text = alerts == 0 ? "Tudo certo" : alerts == 1 ? "1 alerta" : $"{alerts} alertas";
        DashHealthBadge.SetResourceReference(Border.BackgroundProperty, alerts == 0 ? "OkSoft" : "WarnSoft");
        DashHealthBadge.SetResourceReference(Border.BorderBrushProperty, alerts == 0 ? "OkSoft" : "WarnSoft");
        DashHealthBadgeText.SetResourceReference(TextBlock.ForegroundProperty, alerts == 0 ? "Ok" : "Warn");
    }
}
