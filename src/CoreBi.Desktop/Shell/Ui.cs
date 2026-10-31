using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CoreBi.Desktop;

/// <summary>
/// Propriedades anexadas do design system: ícone (glifo Segoe Fluent), legenda, texto de espera e sufixo de unidade.
/// Os templates de Theme.xaml leem estes valores; as páginas só os declaram.
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string value) => d.SetValue(IconProperty, value);

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.RegisterAttached(
        "Caption", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetCaption(DependencyObject d) => (string)d.GetValue(CaptionProperty);
    public static void SetCaption(DependencyObject d, string value) => d.SetValue(CaptionProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.RegisterAttached(
        "Suffix", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetSuffix(DependencyObject d) => (string)d.GetValue(SuffixProperty);
    public static void SetSuffix(DependencyObject d, string value) => d.SetValue(SuffixProperty, value);

    /// <summary>
    /// Botão de iniciar/parar: o mesmo lugar troca de papel (ação principal ↔ interromper),
    /// com cor, glifo e texto coerentes. Assim nenhum teste depende só do texto para mostrar que está rodando.
    /// </summary>
    public static void SetRunning(System.Windows.Controls.Button button, bool running, string idleText, string runningText = "Parar", string idleIcon = "\uE768")
    {
        if (button.TryFindResource(running ? "StopButton" : "PrimaryButton") is Style style) button.Style = style;
        SetIcon(button, running ? "\uE71A" : idleIcon);
        button.Content = running ? runningText : idleText;
    }

    /// <summary>
    /// Selo de veredito (estilo Badge com um LED e um texto dentro): tone \u00E9 "Ok", "Warn", "Danger" ou "" (sem dados).
    /// Saud\u00E1vel fica em tinta com o LED verde; aten\u00E7\u00E3o e cr\u00EDtico ganham fundo e texto na cor, porque pedem que algu\u00E9m olhe.
    /// </summary>
    public static void SetTone(System.Windows.Controls.Border badge, string tone)
    {
        bool alert = tone is "Warn" or "Danger";
        string soft = tone + "Soft";
        if (alert)
        {
            badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, soft);
            badge.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, soft);
        }
        else
        {
            badge.ClearValue(System.Windows.Controls.Border.BackgroundProperty); // o estilo Badge é transparente com fio
            badge.ClearValue(System.Windows.Controls.Border.BorderBrushProperty);
        }
        if (badge.Child is not System.Windows.Controls.Panel panel) return;
        foreach (var child in panel.Children)
        {
            if (child is System.Windows.Shapes.Ellipse led)
                led.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, tone.Length > 0 ? tone : "Faint");
            else if (child is System.Windows.Controls.TextBlock text)
                text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, alert ? tone : tone == "Ok" ? "Fg" : "Muted");
        }
    }

    /// <summary>Raio dos pain\u00E9is (RadiusPanel), para recortar cantos desenhados em c\u00F3digo.</summary>
    public static double PanelRadius(FrameworkElement element) =>
        element.TryFindResource("RadiusPanel") is CornerRadius r ? r.TopLeft : 10;
}

/// <summary>Texto vazio some do layout (rótulos opcionais em templates).</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object value, Type target, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Rótulos do motor chegam em caixa alta ("SAUDÁVEL", "INJOGÁVEL") porque o relatório usa esse formato;
/// na interface os selos ficam em caixa de frase ("Saudável").
/// </summary>
public sealed class SentenceCaseConverter : IValueConverter
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public object Convert(object value, Type target, object parameter, CultureInfo culture)
    {
        if (value is not string s || s.Length == 0) return value ?? "";
        string lower = s.ToLower(PtBr);
        return char.ToUpper(lower[0], PtBr) + lower[1..];
    }
    public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Ocupa o espaço que a página der sem pedir altura própria (além do MinHeight). Serve para um Viewbox numa linha "*":
/// sozinho, ele pediria a altura da largura toda e faria a página rolar com a janela maximizada.
/// </summary>
public sealed class FillBox : System.Windows.Controls.Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        // Mede o filho no mínimo; no arranjo um Viewbox cresce sozinho até o espaço que a página der.
        Child?.Measure(new Size(constraint.Width, Math.Min(constraint.Height, MinHeight)));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}

/// <summary>
/// Pilha vertical que reparte a sobra de altura igualmente entre os filhos (cada um cresce e centraliza o conteúdo),
/// em vez de deixar um vão vazio no fim do painel. Sem sobra, comporta-se como um StackPanel.
/// </summary>
public sealed class SpreadPanel : System.Windows.Controls.Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double w = 0, h = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            w = Math.Max(w, child.DesiredSize.Width);
            h += child.DesiredSize.Height;
        }
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double used = visible.Sum(c => c.DesiredSize.Height);
        double extra = visible.Count > 0 ? Math.Max(0, final.Height - used) / visible.Count : 0;
        double y = 0;
        foreach (var child in visible)
        {
            double h = child.DesiredSize.Height + extra;
            child.Arrange(new Rect(0, y, final.Width, h));
            y += h;
        }
        return final;
    }
}

/// <summary>
/// Linhas de itens que dividem a sobra de largura em vãos iguais, inclusive o vão antes do primeiro item (o último encosta na direita);
/// quando não cabem, quebram em mais linhas, cada uma distribuída do mesmo jeito.
/// </summary>
public sealed class JustifyPanel : System.Windows.Controls.Panel
{
    private List<List<UIElement>> Lines(double width)
    {
        var lines = new List<List<UIElement>> { new() };
        double x = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            double w = c.DesiredSize.Width;
            if (lines[^1].Count > 0 && x + w > width) { lines.Add(new()); x = 0; }
            lines[^1].Add(c);
            x += w;
        }
        return lines;
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement c in InternalChildren) c.Measure(new Size(double.PositiveInfinity, available.Height));
        var lines = Lines(available.Width);
        double h = lines.Sum(l => l.Count == 0 ? 0 : l.Max(c => c.DesiredSize.Height));
        double w = lines.Max(l => l.Sum(c => c.DesiredSize.Width));
        return new Size(double.IsInfinity(available.Width) ? w : Math.Min(w, available.Width), h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        foreach (var line in Lines(final.Width))
        {
            if (line.Count == 0) continue;
            double lineH = line.Max(c => c.DesiredSize.Height);
            double gap = line.Count > 1 ? Math.Max(0, (final.Width - line.Sum(c => c.DesiredSize.Width)) / line.Count) : 0;
            double x = gap;
            foreach (var c in line)
            {
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, lineH));
                x += c.DesiredSize.Width + gap;
            }
            y += lineH;
        }
        return final;
    }
}
