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
    /// Chave, cor no tema escuro, cor no tema claro. Esta tabela é a única fonte das cores do app (docs/DESIGN.md): o painel é
    /// grafite (ou papel), a marca acende o que é interativo e cada componente de hardware tem a sua cor e uma variante suave
    /// (o fundo claro das fichas). Tudo é cor chapada: o app não usa degradês nem brilhos.
    /// </summary>
    private static readonly (object Key, string Dark, string Light)[] Palette =
    {
        // Superfícies: bancada → painel → elevado
        ("Bg", "#0E1214", "#F1F4F6"),
        ("Sidebar", "#0A0D0F", "#EAEEF1"),
        ("Surface", "#15191C", "#FFFFFF"),
        ("Raised", "#1C2022", "#F6F8FA"),
        ("Hover", "#1F2427", "#ECEFF2"),
        ("Selected", "#282D31", "#E2E7EA"),
        ("Well", "#050708", "#101315"),
        ("Scrim", "#B8050708", "#80182228"),
        // Fios
        ("Line", "#24282B", "#DCE0E3"),
        ("LineStrong", "#353A3D", "#C4CACD"),
        // Texto
        ("Fg", "#E6EAEC", "#182228"),
        ("Muted", "#A3AAAE", "#525D62"),
        ("Faint", "#737A7E", "#7A8388"),
        // Sinal: a marca. Ação principal, controles ligados, item escolhido na navegação, foco e o que está ao vivo
        ("Signal", "#27C9BC", "#0A857C"),
        ("SignalHover", "#4DD9CD", "#08766E"),
        ("SignalDeep", "#12AA9D", "#076B64"),
        ("SignalFg", "#03201D", "#FFFFFF"),
        ("SignalSoft", "#2427C9BC", "#1A0A857C"),
        ("Focus", "#5FDCCF", "#0A857C"),
        // Página aberta na navegação: o verde-água da marca, bem escuro, num fundo só
        ("NavSel", "#10373A", "#CFE5E4"),
        // Tinta: leituras neutras (bateria)
        ("Ink", "#E3E7E9", "#1A242A"),
        ("InkFg", "#101315", "#FAFCFD"),
        // Componentes de hardware: a cor de cada peça em todas as telas (gráficos, medidores, anéis, legendas, ícone) e a
        // variante suave, o fundo claro da ficha do componente (Soft)
        ("Cpu", "#1F8A48", "#17703A"),
        ("CpuSoft", "#1A1F8A48", "#1417703A"),
        ("Gpu", "#E8894A", "#C55F1D"),
        ("GpuSoft", "#1AE8894A", "#14C55F1D"),
        ("Memory", "#B296FF", "#8A66D9"),
        ("MemorySoft", "#1AB296FF", "#148A66D9"),
        ("Virtual", "#7A5AC8", "#5B3FA6"),
        ("VirtualSoft", "#1E7A5AC8", "#145B3FA6"),
        ("Storage", "#4A82CC", "#1F5FA8"),
        ("StorageSoft", "#1A4A82CC", "#141F5FA8"),
        ("Board", "#C8A370", "#9A7430"),
        ("BoardSoft", "#1AC8A370", "#149A7430"),
        ("Net", "#E3D45B", "#9A8700"),
        ("NetSoft", "#18E3D45B", "#149A8700"),
        // Rede: o download na cor da rede e o upload no sinal
        ("NetDown", "#E3D45B", "#9A8700"),
        ("NetUp", "#27C9BC", "#0A857C"),
        ("Steel", "#8696A2", "#6A7D8A"),
        ("SteelSoft", "#208696A2", "#186A7D8A"),
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
        (SystemColors.HighlightBrushKey, "#282D31", "#E2E7EA"),
        (SystemColors.InactiveSelectionHighlightBrushKey, "#282D31", "#E2E7EA"),
    };

    public static void Apply(bool light)
    {
        IsLight = light;
        var res = Application.Current.Resources;
        foreach (var (key, dark, lightHex) in Palette)
            res[key] = Frozen(new SolidColorBrush(Parse(light ? lightHex : dark))); // o XAML usa DynamicResource: trocar o pincel atualiza tudo
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>Pincel atual da paleta (para gráficos desenhados em código).</summary>
    public static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.FindResource(key);

    /// <summary>Mesma cor do pincel, com outra opacidade (áreas de gráfico).</summary>
    public static Color WithAlpha(string key, byte alpha)
    {
        var c = Brush(key).Color;
        return Color.FromArgb(alpha, c.R, c.G, c.B);
    }

    /// <summary>Área sob uma linha de gráfico: o canal numa opacidade só, chapado.</summary>
    public static SolidColorBrush Tint(string key, byte alpha)
    {
        var brush = new SolidColorBrush(WithAlpha(key, alpha));
        brush.Freeze();
        return brush;
    }
}
