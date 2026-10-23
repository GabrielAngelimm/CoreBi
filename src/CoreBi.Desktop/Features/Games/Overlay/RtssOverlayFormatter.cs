using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CoreBi.Engine.Games;

namespace CoreBi.Desktop;

/// <summary>Projeta o painel canônico em camadas RTSS, sem outro layout baseado no fluxo e fonte do perfil.</summary>
internal static class RtssOverlayFormatter
{
    internal static RtssContent Format(OverlayPanel panel, string corner, double scale, double opacity,
        int viewportWidth, int viewportHeight, bool nativeFrames = false)
    {
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        panel.UpdateLayout();
        double factor = Math.Clamp(double.IsFinite(scale) ? scale : 1, 1, 2.5) * VisualTreeHelper.GetDpi(panel).DpiScaleX;
        int width = Pixel(panel.ActualWidth * factor), height = Pixel(panel.ActualHeight * factor);
        int margin = Pixel(12 * VisualTreeHelper.GetDpi(panel).DpiScaleX);
        int left = corner.EndsWith('R') ? Math.Max(0, viewportWidth - width - margin) : margin;
        int top = corner.StartsWith('B') ? Math.Max(0, viewportHeight - height - margin) : margin;
        int alpha = (int)Math.Round(Math.Clamp(double.IsFinite(opacity) ? opacity : 1, 0.3, 1) * 255);
        // FNT (RTSS 7.3.7+) fixa a fonte e zoom 1 só neste conteúdo; não altera o perfil do usuário.
        var text = new StringBuilder("<FNT=Bahnschrift SemiCondensed,-13,400,1>", 4096);
        int layer = 0;
        string Color(Brush? brush)
        {
            var c = brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;
            return $"<C={Math.Max(1, c.A * alpha / 255):X2}{c.R:X2}{c.G:X2}{c.B:X2}>";
        }
        void Position(FrameworkElement? element, double x = 0, double y = 0)
        {
            if (element != null)
            {
                var point = element.TranslatePoint(new Point(), panel);
                x += point.X; y += point.Y;
            }
            PositionPixels(Pixel(x * factor), Pixel(y * factor));
        }
        void PositionPixels(int x, int y)
        {
            text.Append(CultureInfo.InvariantCulture, $"<P={left + x},{top + y}><L{layer++}><S=100>");
        }
        var box = (Border)panel.FindName("Box");
        int radius = Math.Clamp(Pixel(box.CornerRadius.TopLeft * factor), 0, Math.Min(width, height) / 2);
        Position(null); text.Append(Color(box.Background)).Append(CultureInfo.InvariantCulture, $"<B={width},{height},R{radius}>");
        int thickness = Math.Max(0, Pixel(box.BorderThickness.Top * factor));
        text.Append(Color(box.BorderBrush));
        void BorderRect(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            PositionPixels(x, y); text.Append(CultureInfo.InvariantCulture, $"<B={w},{h}>");
        }
        // O RTSS arredonda preenchimentos. O contorno usa faixas que não se sobrepõem entre si;
        // empilhar dois cartões preenchidos tornaria o fundo mais opaco quando o usuário reduz a opacidade.
        BorderRect(radius, 0, width - 2 * radius, thickness);
        BorderRect(radius, height - thickness, width - 2 * radius, thickness);
        BorderRect(0, radius, thickness, height - 2 * radius);
        BorderRect(width - thickness, radius, thickness, height - 2 * radius);
        for (int y = 0; y < radius && thickness > 0; y++)
        {
            double dy = radius - y - 0.5, innerRadius = Math.Max(0, radius - thickness);
            int outer = Pixel(radius - Math.Sqrt(radius * radius - dy * dy));
            int inner = dy >= innerRadius ? radius : Pixel(radius - Math.Sqrt(innerRadius * innerRadius - dy * dy));
            BorderRect(outer, y, inner - outer, 1);
            BorderRect(width - inner, y, inner - outer, 1);
            BorderRect(outer, height - y - 1, inner - outer, 1);
            BorderRect(width - inner, height - y - 1, inner - outer, 1);
        }
        foreach (var element in Descendants(panel))
        {
            if (!VisibleWithinPanel(element, panel)) continue;
            if (element is TextBlock block)
            {
                string value = nativeFrames && block.Name == "OFps" ? "<FR>"
                    : nativeFrames && block.Name == "OFrameTime" ? "<FT> ms"
                    : nativeFrames && block.Name == "OLow" ? "1% low -" : SafeText(block.Text);
                if (string.IsNullOrWhiteSpace(value)) continue;
                Position(block);
                int fontSize = Pixel(block.FontSize / 13 * factor * 100);
                text.Append(Color(block.Foreground)).Append(CultureInfo.InvariantCulture, $"<S={fontSize}>").Append(value);
            }
            else if (element is Border { Child: null, Background: SolidColorBrush } bar && bar.ActualWidth > 0 && bar.ActualHeight > 0)
            {
                Position(bar); text.Append(Color(bar.Background)).Append(CultureInfo.InvariantCulture,
                    $"<B={Math.Max(1, Pixel(bar.ActualWidth * factor))},{Math.Max(1, Pixel(bar.ActualHeight * factor))}>");
            }
        }
        byte[] objects = [];
        var graph = (Canvas)panel.FindName("OGraph");
        if (graph.Visibility == Visibility.Visible)
        {
            var line = graph.Children.OfType<Polyline>().Single();
            objects = Graph(line.Points, Pixel(graph.Width * factor), Pixel(graph.Height * factor));
            Position(graph); text.Append(Color(line.Stroke)).Append("<OBJ=0>");
            var baseline = graph.Children.OfType<Line>().Single();
            Position(graph, 0, graph.Height - 1); text.Append(Color(baseline.Stroke))
                .Append(CultureInfo.InvariantCulture, $"<B={Pixel(graph.Width * factor)},1>");
        }
        text.Append("<S><C>");
        return new RtssContent(text.ToString(), objects);
    }

    private static int Pixel(double value) => (int)Math.Round(value);
    private static bool VisibleWithinPanel(FrameworkElement element, OverlayPanel panel)
    {
        DependencyObject? current = element;
        while (current != null && current != panel)
        {
            if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            current = VisualTreeHelper.GetParent(current);
        }
        return true;
    }
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    internal static string SafeText(string value) => new(value.Select(c => c switch
    {
        '<' => '(',
        '>' => ')',
        '\r' or '\n' or '\t' => ' ',
        '—' or '–' => '-',
        '…' => '.',
        _ => !char.IsControl(c) && c is >= ' ' and <= '\u00FF' ? c : '?'
    }).Take(160).ToArray());

    private static byte[] Graph(PointCollection points, int width, int height)
    {
        var samples = points.Where(p => double.IsFinite(p.Y)).TakeLast(120).Select(p => Math.Clamp(height * (1 - p.Y / 30), 0, height)).ToArray();
        var data = new byte[36 + samples.Length * sizeof(float)];
        void UInt(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
        UInt(0, 0x47523030); UInt(4, (uint)data.Length);
        UInt(8, (uint)width); UInt(12, (uint)height);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(28), Math.Max(1, height));
        UInt(32, (uint)samples.Length);
        for (int i = 0; i < samples.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(36 + i * sizeof(float)), (float)samples[i]);
        return data;
    }
}
