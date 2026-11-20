using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Peças comuns das páginas de Ferramentas (Inicialização, Instalar apps, Drivers): o console de tarefa (estilo TaskConsole),
/// que fica ao lado do título em janelas largas e embaixo dele nas estreitas, como o de Rede, e as tabelas feitas à mão, cujo
/// cabeçalho de colunas acompanha a largura útil da lista quando a barra de rolagem aparece.
/// </summary>
internal static class ToolPage
{
    /// <summary>Largura do cabeçalho abaixo da qual o console desce para baixo do título.</summary>
    private const double NarrowHeader = 1040;

    /// <summary>Põe o console ao lado do título ou embaixo dele; devolve o novo estado (estreito ou não).</summary>
    public static bool PlaceConsole(Grid header, FrameworkElement console, bool? wasNarrow)
    {
        bool narrow = header.ActualWidth < NarrowHeader;
        if (narrow == wasNarrow) return narrow;
        Grid.SetRow(console, narrow ? 1 : 0);
        Grid.SetColumn(console, narrow ? 0 : 1);
        Grid.SetColumnSpan(console, narrow ? 2 : 1);
        console.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(40, 0, 0, 0);
        return narrow;
    }

    /// <summary>
    /// Leva a barra até a fração nova: ao avançar desliza 420 ms com desaceleração; ao recomeçar (ou com "Mostrar animações
    /// no Windows" desligado) vai direto ao valor. Nada de animação contínua.
    /// </summary>
    public static void SetProgress(ScaleTransform fill, double fraction)
    {
        fraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        if (!SystemParameters.ClientAreaAnimation || fraction <= fill.ScaleX)
        {
            fill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            fill.ScaleX = fraction;
            return;
        }
        fill.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(fraction, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Estado e detalhe do console; o texto inteiro fica na dica, porque a linha corta o que não cabe.</summary>
    public static void Say(TextBlock status, TextBlock detail, string text, string more = "")
    {
        // Só a primeira linha cabe no console; o resto (lista de falhas do Windows Update, por exemplo) fica na dica.
        string first = text.Split('\n')[0].Trim();
        status.Text = first;
        status.ToolTip = text.Contains('\n') || first.Length > 90 ? text : null;
        detail.Text = more;
        detail.ToolTip = more.Length > 50 ? more : null;
    }

    /// <summary>O cabeçalho de colunas recua a largura da barra de rolagem da lista, para as colunas ficarem alinhadas.</summary>
    public static void AlignHead(ScrollViewer list, FrameworkElement head, Thickness baseMargin)
    {
        double bar = list.ComputedVerticalScrollBarVisibility == Visibility.Visible ? Math.Max(0, list.ActualWidth - list.ViewportWidth) : 0;
        var margin = new Thickness(baseMargin.Left, baseMargin.Top, baseMargin.Right + bar, baseMargin.Bottom);
        if (head.Margin != margin) head.Margin = margin;
    }
}
