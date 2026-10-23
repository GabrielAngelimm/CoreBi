using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using CoreBi.Desktop;
using CoreBi.Engine.Games;

internal static partial class Program
{
    private static readonly HashSet<string> OverlayTestItems = new()
    {
        "fps", "frametime", "low1", "graph", "gpu_load", "gpu_temp", "gpu_clock", "gpu_power",
        "ram_used", "ram_pct", "game", "clock", "hwnames"
    };

    private static OverlayLine[] OverlayTestLines(int value) =>
    [
        new("GPU", [$"{value}%", $"{value} °C", $"{value * 10} MHz", $"{value} W"], "RTX 4060 Ti"),
        new("RAM", [$"{value / 10.0:0.0} GB", $"{value}%"], "32 GB")
    ];

    private static void LayoutOverlay(OverlayPanel panel)
    {
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        panel.UpdateLayout();
    }

    private static void OverlayRenderingTests()
    {
        Check("overlay: readings keep their visual objects between samples", () =>
        {
            var panel = new OverlayPanel();
            var frames = new FrameStats(1, "forhonor", 99, 10.1, 80, [10, 11, 10]);
            panel.Update(frames, OverlayTestLines(99), OverlayTestItems, "18:00"); LayoutOverlay(panel);
            var rows = (ItemsControl)panel.FindName("ORows");
            var row = rows.ItemContainerGenerator.ContainerFromIndex(0);
            var graph = (Canvas)panel.FindName("OGraph");
            var line = graph.Children.OfType<System.Windows.Shapes.Polyline>().Single();
            panel.Update(frames with { Fps = 100 }, OverlayTestLines(100), OverlayTestItems, "18:01"); LayoutOverlay(panel);
            Require(ReferenceEquals(row, rows.ItemContainerGenerator.ContainerFromIndex(0)), "Sensor rows were recreated on a data tick");
            Require(ReferenceEquals(line, graph.Children.OfType<System.Windows.Shapes.Polyline>().Single()), "Graph was recreated on a data tick");
            Require(VisualDescendants<TextBlock>(panel).Count(t => t.Text == "100%") == 2, "Reused text objects did not display new values");
            return new { sensorRowsReused = true, graphReused = true };
        });

        Check("overlay: digit boundaries and missing FPS do not move the readouts", () =>
        {
            var panel = new OverlayPanel();
            var sizes = new List<Size>();
            var positions = new List<Point[]>();
            foreach (int value in new[] { 9, 99, 100, 999, 9 })
            {
                panel.Update(new FrameStats(1, "forhonor", value, 1000.0 / value, value, [10, 11, 10]),
                    OverlayTestLines(value), OverlayTestItems, "18:00"); LayoutOverlay(panel);
                sizes.Add(panel.RenderSize);
                positions.Add(VisualDescendants<TextBlock>(panel).Select(t => t.TranslatePoint(new Point(), panel)).ToArray());
            }
            panel.Update(null, OverlayTestLines(9), OverlayTestItems, "18:00"); LayoutOverlay(panel);
            sizes.Add(panel.RenderSize);
            Require(sizes.All(s => s == sizes[0]), $"Overlay changed size: {string.Join("; ", sizes)}");
            Require(positions.All(p => p.SequenceEqual(positions[0])), "Readouts shifted at a digit boundary");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Output, "overlay-layout.png"))) encoder.Save(file);
            return new { width = sizes[0].Width, height = sizes[0].Height, samples = sizes.Count };
        });
        Check("overlay: selection changes rebuild once and sparse presets keep their size", () =>
        {
            var panel = new OverlayPanel();
            var frames = new FrameStats(1, "forhonor", 9, 111.1, 7, [10, 11]);
            foreach (var items in new[]
            {
                new HashSet<string> { "fps", "frametime", "low1" },
                new HashSet<string> { "fps", "low1" },
                new HashSet<string> { "ram_used", "ram_pct" }
            })
            {
                var lines = items.Contains("ram_used") ? new OverlayLine[] { new("RAM", ["9,9 GB", "9%"]) } : Array.Empty<OverlayLine>();
                panel.Update(frames, lines, items, null); LayoutOverlay(panel);
                var size = panel.RenderSize;
                panel.Update(frames with { Fps = 1000, Low1Fps = 999 }, lines, items, null); LayoutOverlay(panel);
                Require(panel.RenderSize == size, $"Sparse preset {string.Join(",", items)} moved across an FPS digit boundary: {size} -> {panel.RenderSize}");
                panel.Update(null, lines, items, null); LayoutOverlay(panel);
                Require(panel.RenderSize == size, "Sparse preset moved while awaiting a game");
            }
            return new { sparsePresets = 3, selectionChanges = true };
        });
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }
}
