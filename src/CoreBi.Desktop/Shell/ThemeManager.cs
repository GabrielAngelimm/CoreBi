using System.Windows;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Troca entre os temas "Grafite" (escuro) e "Papel" (claro) substituindo os pincéis da paleta (Theme.xaml). O XAML os
/// referencia com DynamicResource, então a troca vale na hora, sem recarregar janelas. Overlay e notificações ficam sempre
/// escuros (aparecem sobre jogos e sobre a área de trabalho).
/// </summary>
public static class ThemeManager
{
    public static bool IsLight { get; private set; }

    /// <summary>Chave, cor no tema escuro, cor no tema claro. Os tons derivam da marca (brand/geometry.json).</summary>
    private static readonly (object Key, string Dark, string Light)[] Palette =
    {
        ("Bg", "#0D141A", "#F2F5F6"),
        ("Sidebar", "#0A1015", "#E8EEF0"),
        ("Surface", "#121B23", "#FFFFFF"),
        ("Raised", "#18232D", "#F7F9FA"),
        ("Hover", "#1B2731", "#E9EFF1"),
        ("Selected", "#21303C", "#DDE7EA"),
        ("Line", "#1F2B35", "#DCE3E7"),
        ("LineStrong", "#2D3D4A", "#C3CFD5"),
        ("Fg", "#E6EEF1", "#122029"),
        ("Muted", "#93A4B0", "#4B5D69"),
        ("Faint", "#61717D", "#7A8A95"),
        ("Accent", "#27C9BC", "#087E78"),
        ("AccentHover", "#45D8CC", "#0A958E"),
        ("AccentSoft", "#2427C9BC", "#1A087E78"),
        ("AccentFg", "#04211E", "#FFFFFF"),
        ("Copper", "#D99067", "#A35F37"),
        ("CopperSoft", "#24D99067", "#1AA35F37"),
        ("Steel", "#7F93A2", "#5D7280"),
        ("Ok", "#5FC385", "#1F7F49"),
        ("OkSoft", "#245FC385", "#1A1F7F49"),
        ("Warn", "#E8B550", "#9A6400"),
        ("WarnSoft", "#26E8B550", "#1F9A6400"),
        ("Danger", "#EF6B5E", "#C0392B"),
        ("DangerSoft", "#26EF6B5E", "#1AC0392B"),
        ("Scrim", "#B8050A0E", "#80182630"),
        ("Well", "#080C10", "#0D141A"),
        ("ThumbBrush", "#2D3D4A", "#C3CFD5"),
        ("ThumbHover", "#4A5D6B", "#9AABB5"),
        ("ThumbDrag", "#6F8391", "#73868F"),
        (SystemColors.ControlBrushKey, "#0D141A", "#F2F5F6"),
        (SystemColors.HighlightBrushKey, "#21303C", "#DDE7EA"),
        (SystemColors.InactiveSelectionHighlightBrushKey, "#21303C", "#DDE7EA"),
    };

    public static void Apply(bool light)
    {
        IsLight = light;
        var res = Application.Current.Resources;
        foreach (var (key, dark, lightHex) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(light ? lightHex : dark);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[key] = brush; // o XAML usa DynamicResource: trocar o pincel atualiza todos os controles na hora
        }
    }

    /// <summary>Pincel atual da paleta (para gráficos desenhados em código).</summary>
    public static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.FindResource(key);

    /// <summary>Mesma cor do pincel, com outra opacidade (áreas de gráfico).</summary>
    public static Color WithAlpha(string key, byte alpha)
    {
        var c = Brush(key).Color;
        return Color.FromArgb(alpha, c.R, c.G, c.B);
    }
}
