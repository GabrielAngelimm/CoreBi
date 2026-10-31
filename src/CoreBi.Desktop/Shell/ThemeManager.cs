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
    /// Chave, cor no tema escuro, cor no tema claro. Estas tabelas são a única fonte das cores do app (docs/DESIGN.md): o painel
    /// é grafite (ou papel), a marca acende o que é interativo e cada componente tem um canal com três tons (fundo do gradiente,
    /// base e brilho). Os canais foram escolhidos em OKLCH e conferidos para daltonismo (protanopia e deuteranopia), aos pares.
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
        ("SignalDeep", "#12AA9D", "#076B64"),
        ("SignalHi", "#6FE3D6", "#12A398"),
        ("SignalFg", "#03201D", "#FFFFFF"),
        ("SignalSoft", "#2427C9BC", "#1A0A857C"),
        ("Focus", "#5FDCCF", "#0A857C"),
        // Tinta: leituras neutras (bateria, sessão atual) e o miolo dos controles
        ("Ink", "#E3E7E9", "#1A242A"),
        ("InkFg", "#101315", "#FAFCFD"),
        // Canais: o que cada componente mede (gráficos, medidores, anéis, legendas e o ícone do componente)
        ("Cpu", "#0CB7A4", "#099485"),
        ("CpuDeep", "#079B8B", "#067A6E"),
        ("CpuHi", "#67D9C8", "#2DB3A3"),
        ("CpuSoft", "#2A0CB7A4", "#1C099485"),
        ("Gpu", "#E8894A", "#C55F1D"),
        ("GpuDeep", "#C9682F", "#A34A10"),
        ("GpuHi", "#F5B07C", "#E07D3E"),
        ("GpuSoft", "#28E8894A", "#1CC55F1D"),
        ("Memory", "#B296FF", "#8A66D9"),
        ("MemoryDeep", "#9573EC", "#6F4CC0"),
        ("MemoryHi", "#D6C6FF", "#A88BEA"),
        ("MemorySoft", "#2AB296FF", "#1C8A66D9"),
        ("Storage", "#4A82CC", "#1F5FA8"),
        ("StorageDeep", "#2F64AE", "#0F4A8C"),
        ("StorageHi", "#86B4EE", "#4A86CC"),
        ("StorageSoft", "#304A82CC", "#1C1F5FA8"),
        ("NetDown", "#0CB7A4", "#099485"),
        ("NetDownDeep", "#079B8B", "#067A6E"),
        ("NetDownHi", "#67D9C8", "#2DB3A3"),
        ("NetUp", "#E8894A", "#C55F1D"),
        ("NetUpDeep", "#C9682F", "#A34A10"),
        ("NetUpHi", "#F5B07C", "#E07D3E"),
        ("Steel", "#8696A2", "#6A7D8A"),
        ("SteelSoft", "#268696A2", "#1C6A7D8A"),
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

    private enum Direction { Horizontal, Vertical }

    /// <summary>
    /// Gradientes nomeados (cores igualmente espaçadas): a luz de cima dos painéis, a tecla acesa da ação principal, o item
    /// escolhido da navegação e o preenchimento dos medidores de cada canal (do tom de fundo ao brilho, como um VU que acende).
    /// </summary>
    private static readonly (string Key, Direction Direction, string[] Dark, string[] Light)[] Gradients =
    {
        ("PanelFill", Direction.Vertical, ["#181C1F", "#14181B"], ["#FFFFFF", "#FBFCFD"]),
        ("PanelEdge", Direction.Vertical, ["#2D3236", "#212528"], ["#E3E7EA", "#D7DCE0"]),
        ("SignalFill", Direction.Vertical, ["#45D6C9", "#1DB5A8"], ["#0F968C", "#087A72"]),
        ("SignalFillHover", Direction.Vertical, ["#6AE2D6", "#2BC4B7"], ["#13A398", "#0A877E"]),
        ("NavSelFill", Direction.Horizontal, ["#2E27C9BC", "#0A27C9BC"], ["#260A857C", "#060A857C"]),
        ("SignalGrad", Direction.Horizontal, ["#12AA9D", "#6FE3D6"], ["#076B64", "#12A398"]),
        ("CpuGrad", Direction.Horizontal, ["#079B8B", "#67D9C8"], ["#067A6E", "#2DB3A3"]),
        ("GpuGrad", Direction.Horizontal, ["#C9682F", "#F5B07C"], ["#A34A10", "#E07D3E"]),
        ("MemoryGrad", Direction.Horizontal, ["#9573EC", "#D6C6FF"], ["#6F4CC0", "#A88BEA"]),
        ("StorageGrad", Direction.Horizontal, ["#2F64AE", "#86B4EE"], ["#0F4A8C", "#4A86CC"]),
    };

    /// <summary>
    /// Brilho de instrumento: uma luz radial suave na cor do canal atrás da leitura principal de um painel (a cor some até
    /// transparente). No tema claro o brilho é mais fraco, como uma tinta.
    /// </summary>
    private static readonly (string Key, string Dark, string Light)[] Glows =
    {
        ("SignalGlow", "#3027C9BC", "#1E0A857C"),
        ("CpuGlow", "#340CB7A4", "#20099485"),
        ("GpuGlow", "#30E8894A", "#1EC55F1D"),
        ("MemoryGlow", "#30B296FF", "#208A66D9"),
        ("StorageGlow", "#384A82CC", "#201F5FA8"),
        ("SteelGlow", "#268696A2", "#1A6A7D8A"),
    };

    public static void Apply(bool light)
    {
        IsLight = light;
        var res = Application.Current.Resources;
        foreach (var (key, dark, lightHex) in Palette)
            res[key] = Frozen(new SolidColorBrush(Parse(light ? lightHex : dark))); // o XAML usa DynamicResource: trocar o pincel atualiza tudo
        foreach (var (key, direction, dark, lightStops) in Gradients)
        {
            var colors = light ? lightStops : dark;
            var brush = direction == Direction.Vertical
                ? new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) }
                : new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i < colors.Length; i++) brush.GradientStops.Add(new GradientStop(Parse(colors[i]), colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
            res[key] = Frozen(brush);
        }
        foreach (var (key, dark, lightHex) in Glows)
        {
            var c = Parse(light ? lightHex : dark);
            var brush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            brush.GradientStops.Add(new GradientStop(c, 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(c.A / 3), c.R, c.G, c.B), 0.55));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            res[key] = Frozen(brush);
        }
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

    /// <summary>
    /// Gradiente de um canal para o que é desenhado em código: do tom de fundo (Deep) ao brilho (Hi) na direção pedida, com a
    /// opacidade dada (255 = cheio). Ex.: Channel("Cpu", 0, 0, 1, 0) para uma barra que acende da esquerda para a direita.
    /// </summary>
    public static LinearGradientBrush Channel(string key, double x0, double y0, double x1, double y1, byte alpha = 255)
    {
        Color Of(string k) { var c = Brush(k).Color; return Color.FromArgb((byte)(c.A * alpha / 255), c.R, c.G, c.B); }
        var brush = new LinearGradientBrush(Of(key + "Deep"), Of(key + "Hi"), new Point(x0, y0), new Point(x1, y1));
        brush.Freeze();
        return brush;
    }

    /// <summary>Traço de gráfico: o canal apagado na amostra mais antiga (à esquerda) e aceso no "agora" (à direita).</summary>
    public static LinearGradientBrush Trace(string key)
    {
        var c = Brush(key).Color;
        var brush = new LinearGradientBrush(Color.FromArgb(0x60, c.R, c.G, c.B), c, 0);
        brush.Freeze();
        return brush;
    }

    /// <summary>Cor entre o fundo (Deep) e o brilho (Hi) de um canal: 0 = Deep, 1 = Hi (anéis em degradê cônico).</summary>
    public static Color Blend(string key, double t)
    {
        Color a = Brush(key + "Deep").Color, b = Brush(key + "Hi").Color;
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>Área sob uma linha de gráfico: o canal com a opacidade dada no topo, sumindo até transparente embaixo.</summary>
    public static LinearGradientBrush AreaFill(string key, byte top, byte bottom = 0)
    {
        var c = Brush(key).Color;
        var brush = new LinearGradientBrush(Color.FromArgb(top, c.R, c.G, c.B), Color.FromArgb(bottom, c.R, c.G, c.B), 90);
        brush.Freeze();
        return brush;
    }
}
