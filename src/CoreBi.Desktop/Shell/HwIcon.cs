using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Ícone de linha sem fundo, na cor do texto (Foreground herdado): as peças do computador desenhadas à mão numa malha de 24 × 24
/// (traço de 1,5, pontas redondas) e, para o resto, o glifo da fonte de ícones do Windows. Kind é o nome de uma peça ("cpu",
/// "gpu", "ram", "vram", "storage", "board", "net", "router", "desktop", "fan", "ramv") ou um glifo (um caractere da Segoe Fluent
/// Icons). O tamanho é o Width e o Height; o desenho acompanha o menor deles.
/// </summary>
public sealed class HwIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(HwIcon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    private const double Cells = 24, Weight = 1.5;

    /// <summary>Um traço do ícone: o contorno (ou o preenchimento, num ponto) e se é tracejado.</summary>
    private readonly record struct Part(Geometry Geometry, bool Dashed = false, bool Filled = false);

    private static readonly Dictionary<string, Part[]> Shapes = Build();

    /// <summary>Se o nome é de uma peça desenhada aqui ou um glifo da fonte de ícones (um caractere da área privada).</summary>
    public static bool IsKnown(string kind) => Shapes.ContainsKey(kind) || (kind.Length == 1 && kind[0] >= '\uE000');

    public HwIcon()
    {
        Focusable = false;
        IsTabStop = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty) InvalidateVisual(); // a cor vem do estilo e muda com o tema
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        string kind = Kind ?? "";
        if (size <= 0 || kind.Length == 0) return;
        Brush brush = Foreground ?? Brushes.Gray;

        if (Shapes.TryGetValue(kind, out var parts))
        {
            double scale = size / Cells;
            dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            var solid = new Pen(brush, Weight) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var dashed = new Pen(brush, Weight) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round, LineJoin = PenLineJoin.Round, DashStyle = new DashStyle(new[] { 0.1, 1.9 }, 0) };
            foreach (var part in parts)
            {
                if (part.Filled) dc.DrawGeometry(brush, null, part.Geometry);
                else dc.DrawGeometry(null, part.Dashed ? dashed : solid, part.Geometry);
            }
            dc.Pop();
            dc.Pop();
            return;
        }

        // Glifo da fonte de ícones do Windows
        var family = TryFindResource("Icons") as FontFamily ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        var text = new FormattedText(kind, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size * 0.94, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
    }

    // ---------------- desenhos ----------------

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static Geometry Parse(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    /// <summary>Retângulo de cantos arredondados (x, y, largura, altura, raio).</summary>
    private static Part Box(double x, double y, double w, double h, double r, bool dashed = false) => new(Parse(
        $"M{N(x + r)},{N(y)} H{N(x + w - r)} A{N(r)},{N(r)} 0 0 1 {N(x + w)},{N(y + r)} V{N(y + h - r)} A{N(r)},{N(r)} 0 0 1 {N(x + w - r)},{N(y + h)} " +
        $"H{N(x + r)} A{N(r)},{N(r)} 0 0 1 {N(x)},{N(y + h - r)} V{N(y + r)} A{N(r)},{N(r)} 0 0 1 {N(x + r)},{N(y)} Z"), dashed);

    private static Part Ring(double cx, double cy, double r) => new(Parse(
        $"M{N(cx - r)},{N(cy)} A{N(r)},{N(r)} 0 1 0 {N(cx + r)},{N(cy)} A{N(r)},{N(r)} 0 1 0 {N(cx - r)},{N(cy)} Z"));

    private static Part Dot(double cx, double cy, double r = 0.95) => new(Parse(
        $"M{N(cx - r)},{N(cy)} A{N(r)},{N(r)} 0 1 0 {N(cx + r)},{N(cy)} A{N(r)},{N(r)} 0 1 0 {N(cx - r)},{N(cy)} Z"), Filled: true);

    private static Part Line(string path, bool dashed = false) => new(Parse(path), dashed);

    private static Dictionary<string, Part[]> Build()
    {
        var pins3 = new[] { 9.5, 12, 14.5 };
        var cpu = new List<Part> { Box(5, 5, 14, 14, 3), Box(9.5, 9.5, 5, 5, 1) };
        foreach (double p in pins3)
        {
            cpu.Add(Line($"M{N(p)},2 V5"));
            cpu.Add(Line($"M{N(p)},19 V22"));
            cpu.Add(Line($"M2,{N(p)} H5"));
            cpu.Add(Line($"M19,{N(p)} H22"));
        }

        var vram = new List<Part> { Box(5, 5, 14, 14, 2.5), Line("M8.5,9.5 H15.5"), Line("M8.5,12 H15.5"), Line("M8.5,14.5 H15.5") };
        foreach (double y in new[] { 8.5, 12, 15.5 })
        {
            vram.Add(Line($"M2,{N(y)} H5"));
            vram.Add(Line($"M19,{N(y)} H22"));
        }

        Part[] Dimm(bool virtualMemory)
        {
            var parts = new List<Part> { Box(2, 6.5, 20, 10, 2, dashed: virtualMemory), Box(5.5, 9.5, 4.5, 4, 0.8), Box(14, 9.5, 4.5, 4, 0.8) };
            foreach (double x in new[] { 5.5, 8, 10.5, 13.5, 16, 18.5 }) parts.Add(Line($"M{N(x)},16.5 V19.5"));
            return parts.ToArray();
        }

        var fan = new List<Part> { Ring(12, 12, 9.25), Dot(12, 12, 1.25) };
        foreach (double angle in new[] { 0.0, 120, 240 })
        {
            var blade = Geometry.Parse("M12,10.7 C9.3,9.4 9.4,5.6 12,3.9 C14.6,5.6 14.7,9.4 12,10.7 Z").Clone();
            blade.Transform = new RotateTransform(angle, 12, 12);
            blade.Freeze();
            fan.Add(new Part(blade));
        }

        return new Dictionary<string, Part[]>
        {
            ["cpu"] = cpu.ToArray(),
            ["gpu"] = new[]
            {
                Box(2.5, 6, 19, 10.5, 2), Ring(8.5, 11.25, 3), Ring(15.5, 11.25, 3), Dot(8.5, 11.25, 0.9), Dot(15.5, 11.25, 0.9),
                Line("M7,16.5 V19.5 H17 V16.5"),
            },
            ["ram"] = Dimm(false),
            ["ramv"] = Dimm(true),
            ["vram"] = vram.ToArray(),
            ["storage"] = new[] { Box(3, 6, 18, 12, 2.5), Line("M3,12.5 H21"), Dot(17.5, 15.25), Line("M6.5,15.25 H11.5") },
            ["board"] = new[] { Box(3, 3, 18, 18, 2.5), Box(6.5, 6.5, 5.5, 5.5, 1), Line("M15,6.5 V12"), Line("M18,6.5 V12"), Line("M6.5,16.5 H17.5") },
            ["net"] = new[]
            {
                Box(9, 3.5, 6, 5, 1), Line("M12,8.5 V12.5"), Line("M5,15.5 V12.5 H19 V15.5"),
                Box(2.5, 15.5, 5, 5, 1), Box(9.5, 15.5, 5, 5, 1), Box(16.5, 15.5, 5, 5, 1), Line("M12,12.5 V15.5"),
            },
            ["router"] = new[] { Box(2.5, 13, 19, 7, 2), Line("M7,13 V6.5"), Line("M17,13 V6.5"), Dot(7, 16.5), Dot(10.5, 16.5) },
            ["desktop"] = new[] { Box(3, 4.5, 18, 11.5, 2), Line("M12,16 V19.5"), Line("M8,19.5 H16") },
            ["fan"] = fan.ToArray(),
        };
    }
}
