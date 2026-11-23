using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>Formatação compartilhada de mostradores (número + unidade menor em tom secundário na mesma linha de base).</summary>
internal static class ReadoutFormat
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Quando algo aconteceu, como as páginas dizem: "hoje às 21:40", "ontem às 18:02" ou "17/11 às 09:12" (outro ano:
    /// "17/11/2025"); compacto, sem o "às" (linhas de histórico).
    /// </summary>
    public static string When(DateTime at, bool compact = false)
    {
        var day = at.Date;
        var today = DateTime.Today;
        string date = day == today ? "hoje" : day == today.AddDays(-1) ? "ontem"
            : at.Year == today.Year ? at.ToString("dd/MM", PtBr) : at.ToString("dd/MM/yyyy", PtBr);
        return compact ? $"{date} {at:HH:mm}" : $"{date} às {at:HH:mm}";
    }

    /// <summary>Duração curta: "45 s", "3 min", "1 h 05 min".</summary>
    /// <summary>
    /// Nome de um teste do Benchmark fora da página (Visão geral e relatório), pela chave gravada no histórico
    /// (<c>rt:&lt;qualidade&gt;:&lt;altura&gt;</c> no ray tracing). Um nome só, para o mesmo resultado não aparecer com dois.
    /// </summary>
    public static string BenchTitle(string test) => test switch
    {
        "render" => "Render de CPU",
        "sim" => "Simulação de jogo (CPU)",
        "gpu" => "Cena de jogo (GPU)",
        "mem" => "Benchmark de memória",
        "disk" => "Benchmark de disco",
        _ when test.StartsWith("rt:", StringComparison.Ordinal) => "Ray tracing",
        _ => test,
    };

    public static string Span(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return $"{Math.Max(0, (int)t.TotalSeconds)} s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min";
        return $"{(int)t.TotalHours} h {t.Minutes:00} min";
    }

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
