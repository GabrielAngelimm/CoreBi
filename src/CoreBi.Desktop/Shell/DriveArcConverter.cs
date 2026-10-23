using System.Windows;
using System.Globalization;

using System.Windows.Data;
using System.Windows.Media;
namespace CoreBi.Desktop;

public sealed class DriveArcConverter : IValueConverter
{
    public object Convert(object value, Type target, object parameter, CultureInfo culture)
    {
        double fraction = Math.Clamp(value is double d ? d : 0, 0, 0.9999);
        if (fraction <= 0) return Geometry.Empty;
        double angle = fraction * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(42, 6) };
        figure.Segments.Add(new ArcSegment(new Point(42 + 36 * Math.Sin(angle), 42 - 36 * Math.Cos(angle)), new Size(36, 36), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry(new[] { figure }); geometry.Freeze(); return geometry;
    }
    public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
