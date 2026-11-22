using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Peças comuns das páginas de Ferramentas (Inicialização, Instalar apps, Drivers): o medidor de progresso (TaskMeter) da barra de
/// ações do cabeçalho, o estado ao lado dele e as tabelas feitas à mão, cujo cabeçalho de colunas acompanha a largura útil da lista
/// quando a barra de rolagem aparece.
/// </summary>
internal static class ToolPage
{
    /// <summary>Leva o medidor da barra de ações até a fração nova (o movimento está em <see cref="HeaderMotion.SetProgress"/>).</summary>
    public static void SetProgress(Border fill, double fraction) => HeaderMotion.SetProgress(fill, fraction);

    /// <summary>A tarefa parou sem terminar (erro, leitura sem etapas): o medidor apaga e fecha já.</summary>
    public static void EndProgress(Border fill) => HeaderMotion.EndProgress(fill);

    /// <summary>Estado e detalhe da barra de ações; o texto inteiro fica na dica, porque a linha corta o que não cabe.</summary>
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
