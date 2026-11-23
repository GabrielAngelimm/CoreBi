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
/// Raio de pílula: metade da altura medida. Os campos de texto usam no contorno para ficar inteiramente redondos em qualquer
/// altura (40 na busca do cabeçalho, 34 nos cartões), como os segmentos e os filtros.
/// </summary>
public sealed class PillRadiusConverter : IValueConverter
{
    public object Convert(object value, Type target, object parameter, CultureInfo culture)
        => new CornerRadius(value is double h && h > 0 ? h / 2 : 0);
    public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
