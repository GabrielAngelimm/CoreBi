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
}

/// <summary>Glifo de cada categoria do inventário (Sistema), pelo título que o coletor devolve.</summary>
public sealed class CategoryIconConverter : IValueConverter
{
    public object Convert(object value, Type target, object parameter, CultureInfo culture) => (value as string) switch
    {
        "Sistema operacional" => "",
        "Placa-mãe e BIOS" => "",
        "Processador" => "",
        "Memória RAM" => "",
        "Placa de vídeo" => "",
        "Armazenamento" => "",
        _ => "",
    };
    public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => throw new NotSupportedException();
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
