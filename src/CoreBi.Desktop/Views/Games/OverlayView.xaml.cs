using CoreBi.Engine.Games;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CoreBi.Desktop.Views;

public partial class OverlayView : UserControl
{
    /// <summary>
    /// Escala da prévia em relação ao overlay real. O monitor desenhado tem 820 de largura para uma tela de 1920:
    /// na proporção exata o texto ficaria ilegível, então a caixa aparece um pouco maior que a "verdadeira".
    /// </summary>
    private const double PreviewFactor = 0.72;
    private static readonly Brush LedOn = Frozen(Color.FromRgb(0x27, 0xC9, 0xBC));
    private static readonly Brush LedOff = Frozen(Color.FromRgb(0x33, 0x41, 0x4C));

    private string? _corner;

    internal MainWindow? Owner { get; set; }
    public OverlayView() => InitializeComponent();
    private void ChkOverlay_Changed(object sender, RoutedEventArgs e) => Owner?.ChkOverlay_Changed(sender, e);
    private void OvOption_Changed(object sender, RoutedEventArgs e) => Owner?.OvOption_Changed(sender, e);
    private void OvPreset_Click(object sender, RoutedEventArgs e) => Owner?.OvPreset_Click(sender, e);

    /// <summary>Com largura (janela maximizada) os dois painéis ficam lado a lado; senão, o que mostrar desce para baixo do monitor.</summary>
    private void OvMain_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        bool wide = e.NewSize.Width >= 1180;
        Grid.SetRow(OvItemsCard, wide ? 0 : 1);
        Grid.SetColumn(OvItemsCard, wide ? 2 : 0);
        Grid.SetColumnSpan(OvItemsCard, wide ? 1 : 3);
        Grid.SetColumnSpan(OvPositionCard, wide ? 1 : 3);
        OvItemsCard.Margin = new Thickness(0, wide ? 0 : 16, 0, 0);
        OvMonitorBox.MinHeight = wide ? 300 : 440;
    }

    /// <summary>Leva a prévia ao canto escolhido, no tamanho escolhido. Trocar de canto tem uma entrada curta.</summary>
    internal void LayoutPreview(string corner, double scale)
    {
        OvPreviewHost.HorizontalAlignment = corner.EndsWith('R') ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        OvPreviewHost.VerticalAlignment = corner.StartsWith('B') ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        OvPreviewScale.ScaleX = OvPreviewScale.ScaleY = PreviewFactor * scale;
        OvCornerName.Text = corner switch
        {
            "TR" => "Superior direito",
            "BL" => "Inferior esquerdo",
            "BR" => "Inferior direito",
            _ => "Superior esquerdo",
        };
        if (_corner != null && _corner != corner && SystemParameters.ClientAreaAnimation)
            OvPreviewHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(180)));
        _corner = corner;
    }

    /// <summary>Atualiza a prévia com os itens marcados. <paramref name="sample"/> = FPS de exemplo (nenhum jogo na frente).</summary>
    internal void ShowPreview(FrameStats f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra, bool sample, bool overlayOn)
    {
        OvPreview.Update(f, lines, show, extra);
        OvPreviewNote.Text = sample ? "Prévia com FPS de exemplo" : $"Prévia ao vivo · {f.ProcessName}";
        OvPreviewNote.ToolTip = sample
            ? "Com o CoreBi na frente não há jogo para medir, então o FPS da prévia é ilustrativo. Os sensores são lidos ao vivo."
            : null;
        OvPreviewDot.SetResourceReference(Shape.FillProperty, sample ? "Faint" : "Ok");
        OvLed.Fill = overlayOn ? LedOn : LedOff;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
