using CoreBi.Engine.Games;
using System.ComponentModel;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CoreBi.Desktop;

/// <summary>Caixa do overlay: FPS, frametime, gráfico e linhas de sensores, só com os itens escolhidos.</summary>
public partial class OverlayPanel : UserControl
{
    private sealed class Readout : INotifyPropertyChanged
    {
        public string Value { get; private set; }
        public double Width { get; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public Readout(string value, double width) { Value = value; Width = width; }
        public void Update(string value)
        {
            if (Value == value) return;
            Value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    private sealed record ReadoutRow(string Label, string? Detail, IReadOnlyList<Readout> Parts);
    private IReadOnlyList<ReadoutRow> _rows = Array.Empty<ReadoutRow>();
    private HashSet<string> _selection = new();
    private readonly Polyline _graphLine = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0x27, 0xC9, 0xBC)),
        StrokeThickness = 1.2
    };

    public OverlayPanel()
    {
        InitializeComponent();
        OFps.MinWidth = TextWidth("9999", OFps.FontSize, OFps.FontWeight);
        OFrameTime.MinWidth = TextWidth("999,9 ms", OFrameTime.FontSize, OFrameTime.FontWeight);
        OLow.MinWidth = Math.Max(TextWidth("1% low 9999"), TextWidth("aguardando jogo"));
        OGraph.Children.Add(new Line
        {
            X1 = 0,
            X2 = OGraph.Width,
            Y1 = OGraph.Height - 0.5,
            Y2 = OGraph.Height - 0.5,
            Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0x82, 0x97, 0xA6))
        });
        OGraph.Children.Add(_graphLine);
    }

    /// <summary>Atualiza a caixa. <paramref name="show"/> = itens escolhidos (chaves de OverlayMetrics).</summary>
    public void Update(FrameStats? f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra)
    {
        static Visibility V(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        bool fps = show.Contains("fps"), ft = show.Contains("frametime");
        OFpsRow.Visibility = V(fps || ft);
        OFps.Visibility = OFpsUnit.Visibility = V(fps);
        OFrameTime.Visibility = V(ft);
        OFrameTime.Margin = new Thickness(fps ? 12 : 0, 0, 0, fps ? 5 : 0);
        OLow.Visibility = V(show.Contains("low1"));
        OGraph.Visibility = V(show.Contains("graph"));

        if (f != null)
        {
            OFps.Text = $"{f.Fps:0}";
            OFrameTime.Text = $"{f.FrameTimeMs:0.0} ms";
            OLow.Text = $"1% low {f.Low1Fps:0}";
        }
        else
        {
            OFps.Text = "—";
            OFrameTime.Text = fps ? "" : "— ms";
            OLow.Text = "aguardando jogo";
        }
        // Rodapé: nome do jogo e/ou hora.
        var foot = new List<string>();
        if (show.Contains("game") && f != null) foot.Add(f.ProcessName);
        if (extra != null) foot.Add(extra);
        // Reserva a linha escolhida mesmo quando o processo em primeiro plano ainda não tem FPS.
        OGame.Text = foot.Count > 0 ? string.Join("  ·  ", foot) : " ";
        OGame.Visibility = V(show.Contains("game") || show.Contains("clock"));

        UpdateRows(lines, show);
        ORows.Visibility = V(lines.Count > 0);
        if (OGraph.Visibility == Visibility.Visible) DrawGraph(f?.RecentFrameTimes);
    }

    private void UpdateRows(IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show)
    {
        bool shapeChanged = !_selection.SetEquals(show) || _rows.Count != lines.Count;
        for (int i = 0; !shapeChanged && i < lines.Count; i++)
            shapeChanged = _rows[i].Label != lines[i].Label || _rows[i].Detail != lines[i].Detail || _rows[i].Parts.Count != lines[i].Parts.Count;
        if (shapeChanged)
        {
            _selection = show.ToHashSet();
            _rows = lines.Select(line =>
            {
                var metrics = OverlayMetrics.All.Where(m => m.Group == line.Label && show.Contains(m.Key)).ToArray();
                var parts = line.Parts.Select((value, i) => new Readout(value,
                    Math.Max(TextWidth(value), TextWidth(i < metrics.Length ? WidthSample(metrics[i].Key) : value)))).ToArray();
                return new ReadoutRow(line.Label, line.Detail, parts);
            }).ToArray();
            ORows.ItemsSource = _rows;
        }
        else
        {
            for (int row = 0; row < lines.Count; row++)
                for (int part = 0; part < lines[row].Parts.Count; part++)
                    _rows[row].Parts[part].Update(lines[row].Parts[part]);
        }
    }

    // Espaço para mudanças de dígitos; tipografia, cores, separadores e ordem permanecem os mesmos.
    private static string WidthSample(string key) => key switch
    {
        "gpu_load" or "cpu_load" or "vram_pct" or "ram_pct" => "100%",
        "cpu_maxcore" => "núcleo 100%",
        "gpu_temp" or "cpu_temp" or "vram_temp" => "100 °C",
        "gpu_hotspot" => "HS 100 °C",
        "gpu_clock" => "9999 MHz",
        "gpu_memclock" => "mem 99999 MHz",
        "cpu_clock" => "9,99 GHz",
        "cpu_clockmax" => "pico 9,99 GHz",
        "gpu_power" or "cpu_power" => "999 W",
        "gpu_volt" => "9,999 V",
        "gpu_fan" => "99999 RPM",
        "vram_used" or "ram_used" => "999,9 GB",
        _ => "—"
    };

    private static double TextWidth(string text, double size = 13, FontWeight? weight = null)
    {
        var readout = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Bahnschrift"),
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            FontStretch = FontStretches.SemiCondensed
        };
        Typography.SetNumeralAlignment(readout, FontNumeralAlignment.Tabular);
        readout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(readout.DesiredSize.Width);
    }

    /// <summary>Frametimes recentes: picos para cima = engasgos.</summary>
    private void DrawGraph(IReadOnlyList<double>? ft)
    {
        double w = OGraph.Width, h = OGraph.Height;
        var points = new PointCollection();
        if (ft == null || ft.Count < 2) { _graphLine.Points = points; return; }

        double max = Math.Max(ft.Max(), ft.Average() * 2); // escala que mostra picos sem achatar o normal
        double step = w / (ft.Count - 1);
        for (int i = 0; i < ft.Count; i++)
            points.Add(new Point(i * step, h - 1 - (h - 2) * Math.Min(1, ft[i] / max)));
        _graphLine.Points = points;
    }
}
