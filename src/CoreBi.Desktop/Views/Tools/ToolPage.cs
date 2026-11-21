using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Peças comuns das páginas de Ferramentas (Inicialização, Instalar apps, Drivers): o medidor de progresso (TaskMeter) da barra de
/// ações do cabeçalho, o estado no subtítulo e as tabelas feitas à mão, cujo cabeçalho de colunas acompanha a largura útil da lista
/// quando a barra de rolagem aparece.
/// </summary>
internal static class ToolPage
{
    /// <summary>
    /// Leva a barra até a fração nova e mostra o medidor, que só existe durante uma tarefa: parado, a barra de ações não tem
    /// linha nenhuma (o espaço fica reservado, para os botões não andarem). A porcentagem aparece quando há trabalho feito. Ao avançar desliza 400 ms com desaceleração; ao recomeçar
    /// (ou com "Mostrar animações no Windows" desligado) vai direto ao valor. Quando a tarefa termina, o trilho cheio apaga
    /// em 300 ms e some. Nada de animação contínua.
    /// </summary>
    public static void SetProgress(Border fill, double fraction)
    {
        var scale = (ScaleTransform)fill.RenderTransform;
        var track = Meter(fill);
        fraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        if (Percent(track) is { } percent) percent.Text = fraction > 0 ? Math.Round(fraction * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "\u2009%" : "";
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
        var track = Meter(fill);
        if (Percent(track) is { } percent) percent.Text = "";
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.ScaleX = 0;
        track.BeginAnimation(UIElement.OpacityProperty, null);
        track.Opacity = 1;
        track.Visibility = Visibility.Hidden;
    }

    /// <summary>O medidor inteiro (trilho e porcentagem): o pai do trilho quando ele está num TaskMeter, senão o próprio trilho.</summary>
    private static FrameworkElement Meter(Border fill)
    {
        var track = (FrameworkElement)fill.Parent;
        return track.Parent is Grid meter && meter.Children.OfType<TextBlock>().Any() ? meter : track;
    }

    private static TextBlock? Percent(FrameworkElement meter) => (meter as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();

    /// <summary>Estado e detalhe do subtítulo; o texto inteiro fica na dica, porque a linha corta o que não cabe.</summary>
    public static void Say(TextBlock status, TextBlock detail, string text, string more = "")
    {
        // Só a primeira linha cabe no subtítulo; o resto (lista de falhas do Windows Update, por exemplo) fica na dica.
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
