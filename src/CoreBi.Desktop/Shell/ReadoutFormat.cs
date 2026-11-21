using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>Formatação compartilhada de mostradores (número + unidade menor em tom secundário na mesma linha de base).</summary>
internal static class ReadoutFormat
{
    public static void Set(TextBlock target, string value, string unit, double unitRatio = 0.4, bool balanced = false, bool dimWhenDash = false)
    {
        target.Inlines.Clear();
        if (dimWhenDash)
        {
            if (value == "—") target.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            else target.ClearValue(TextBlock.ForegroundProperty);
        }

        double unitSize = Math.Max(12, Math.Round(target.FontSize * unitRatio));
        if (balanced && unit.Length > 0)
        {
            target.Inlines.Add(new Run(unit + " ")
            {
                FontSize = unitSize,
                Foreground = Brushes.Transparent,
                FontFamily = (FontFamily)Application.Current.FindResource("UiFont"),
            });
        }
        target.Inlines.Add(new Run(value));
        if (unit.Length == 0) return;

        var unitRun = new Run(" " + unit) { FontSize = unitSize };
        unitRun.SetResourceReference(TextElement.ForegroundProperty, "Muted");
        unitRun.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        target.Inlines.Add(unitRun);
    }
}
