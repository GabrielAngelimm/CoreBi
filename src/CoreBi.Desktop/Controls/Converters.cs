using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CoreBi.Desktop;

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
