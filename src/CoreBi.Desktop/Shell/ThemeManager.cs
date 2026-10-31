using System.Windows;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Troca entre os temas "Grafite" (escuro) e "Papel" (claro) substituindo os pincéis da paleta (Theme.xaml). O XAML os
/// referencia com DynamicResource, então a troca vale na hora, sem recarregar janelas. Overlay e notificações usam a paleta
/// fixa Hud* (aparecem sobre jogos e sobre a área de trabalho).
/// </summary>
public static class ThemeManager
{
    public static bool IsLight { get; private set; }

    /// <summary>
    /// Chave, cor no tema escuro, cor no tema claro. Esta tabela é a única fonte das cores do app (docs/DESIGN.md): o painel
    /// é neutro (grafite ou papel, levemente frio), as ações são tinta e a cor fica para o que é medido. Os canais foram
    /// escolhidos em OKLCH com a mesma força e conferidos para daltonismo (protanopia e deuteranopia) aos pares.
    /// </summary>
    private static readonly (object Key, string Dark, string Light)[] Palette =
    {
        // Superfícies: bancada → painel → elevado
        ("Bg", "#0E1214", "#F1F4F6"),
        ("Sidebar", "#0A0D0F", "#ECEFF1"),
        ("Surface", "#15191C", "#FFFFFF"),
        ("Raised", "#1C2022", "#F6F8FA"),
        ("Hover", "#1E2325", "#ECEFF1"),
        ("Selected", "#282D30", "#E1E6E8"),
        ("Well", "#050708", "#101315"),
        ("Scrim", "#B8050708", "#80182228"),
        // Fios
        ("Line", "#24282B", "#DCE0E3"),
        ("LineStrong", "#353A3D", "#C4CACD"),
        // Texto
        ("Fg", "#E6EAEC", "#182228"),
        ("Muted", "#A3AAAE", "#525D62"),
        ("Faint", "#737A7E", "#798287"),
        // Tinta: ação principal, controles ligados e marcados, foco de teclado
        ("Ink", "#E3E7E9", "#1A242A"),
        ("InkHover", "#F8FAFC", "#2C373D"),
        ("InkFg", "#101315", "#FAFCFD"),
        ("Focus", "#C9CFD2", "#33424A"),
        // Sinal: a marca e o que está ao vivo
        ("Signal", "#27C9BC", "#087E78"),
        ("SignalSoft", "#2427C9BC", "#1A087E78"),
        // Canais: o que cada componente mede (gráficos, medidores, anéis e legendas)
        ("Cpu", "#07A293", "#0A8E82"),
        ("Gpu", "#C77E50", "#A25A30"),
        ("Memory", "#A183D4", "#9274C3"),
        ("Storage", "#4E719D", "#3A5B8D"),
        ("NetDown", "#07A293", "#0A8E82"),
        ("NetUp", "#C77E50", "#A25A30"),
        ("Steel", "#8696A2", "#6A7D8A"),
        // Veredito: só quando algo pede atenção ganha cor cheia; saudável é um LED discreto
        ("Ok", "#6FC889", "#25804A"),
        ("OkSoft", "#226FC889", "#1A25804A"),
        ("Warn", "#EDB755", "#9A6400"),
        ("WarnSoft", "#26EDB755", "#1F9A6400"),
        ("Danger", "#EE6E63", "#C0392B"),
        ("DangerSoft", "#26EE6E63", "#1AC0392B"),
        // Rolagem
        ("ThumbBrush", "#353A3D", "#C4CACD"),
        ("ThumbHover", "#50575B", "#9EA6AB"),
        ("ThumbDrag", "#6E7579", "#7F888D"),
        (SystemColors.ControlBrushKey, "#0E1214", "#F1F4F6"),
        (SystemColors.HighlightBrushKey, "#282D30", "#E1E6E8"),
        (SystemColors.InactiveSelectionHighlightBrushKey, "#282D30", "#E1E6E8"),
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
