using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Peças comuns das páginas de Ferramentas (Inicialização, Instalar apps, Drivers): o console de tarefa (estilo TaskConsole),
/// que fica ao lado do título (centrado na altura dele) em janelas largas e embaixo dele nas estreitas, e as tabelas feitas à mão, cujo
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
    /// Leva a barra até a fração nova e mostra o trilho, que só existe durante uma tarefa: parado, o console não tem linha
    /// nenhuma (o espaço fica reservado, para a altura não mudar). Ao avançar desliza 400 ms com desaceleração; ao recomeçar
    /// (ou com "Mostrar animações no Windows" desligado) vai direto ao valor. Quando a tarefa termina, o trilho cheio apaga
    /// em 300 ms e some. Nada de animação contínua.
    /// </summary>
    public static void SetProgress(Border fill, double fraction)
    {
        var scale = (ScaleTransform)fill.RenderTransform;
        var track = (FrameworkElement)fill.Parent;
        fraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        track.BeginAnimation(UIElement.OpacityProperty, null);
        track.Opacity = 1;
        track.Visibility = Visibility.Visible;
        bool animate = SystemParameters.ClientAreaAnimation;
        if (!animate || fraction <= scale.ScaleX)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.ScaleX = fraction;
        }
        else
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(fraction, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        if (fraction < 1) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(animate ? 300 : 0)) { BeginTime = TimeSpan.FromMilliseconds(animate ? 900 : 600) };
        fade.Completed += (_, _) =>
        {
            if (track.Opacity > 0.01) return;
            EndProgress(fill);
        };
        track.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>A tarefa parou sem terminar (erro, leitura sem etapas): o trilho some na hora e a barra volta ao começo.</summary>
    public static void EndProgress(Border fill)
    {
        var scale = (ScaleTransform)fill.RenderTransform;
        var track = (FrameworkElement)fill.Parent;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.ScaleX = 0;
        track.BeginAnimation(UIElement.OpacityProperty, null);
        track.Opacity = 1;
        track.Visibility = Visibility.Hidden;
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
