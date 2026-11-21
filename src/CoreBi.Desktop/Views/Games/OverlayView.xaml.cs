using CoreBi.Engine.Games;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop.Views;

public partial class OverlayView : UserControl
{
    /// <summary>
    /// Escala da prévia em relação ao overlay real: largura da tela desenhada (798) pela do monitor principal,
    /// com 40 % de folga para o texto continuar legível; mesmo no tamanho Grande a caixa cabe no quarto da tela do seu canto.
    /// </summary>
    private static readonly double PreviewFactor = 798.0 / Math.Max(1280, SystemParameters.PrimaryScreenWidth) * 1.4;

    private string? _corner;

    internal MainWindow? Owner { get; set; }
    public OverlayView()
    {
        InitializeComponent();
        // A nota só vale sem administrador: com ele, temperatura, clock e consumo da CPU têm leitura
        OvAdminNote.Visibility = CoreBi.Engine.Infrastructure.ProcessElevation.IsElevated ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ChkOverlay_Changed(object sender, RoutedEventArgs e) => Owner?.ChkOverlay_Changed(sender, e);
    private void OvOption_Changed(object sender, RoutedEventArgs e) => Owner?.OvOption_Changed(sender, e);
    private void OvOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OvOpacityText == null || OvPreviewHost == null) return; // disparado durante InitializeComponent
        OvOpacityText.Text = $"{e.NewValue:0} %";
        OvPreviewHost.Opacity = OpacityOf(e.NewValue);
        Owner?.OvOpacity_Changed(OpacityOf(e.NewValue));
    }

    /// <summary>Roda do mouse sobre o controle ajusta a opacidade (5 pontos por clique) em vez de rolar a página.</summary>
    private void OvOpacity_Wheel(object sender, MouseWheelEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 5;
        OvOpacity.Value = Math.Clamp(OvOpacity.Value + Math.Sign(e.Delta) * step, OvOpacity.Minimum, OvOpacity.Maximum);
        e.Handled = true;
    }

    private static double OpacityOf(double percent) => Math.Clamp(percent / 100, 0.3, 1);

    /// <summary>Posição inicial do controle.</summary>
    internal void SetOpacity(double opacity)
    {
        OvOpacity.Value = Math.Round(opacity * 100);
        OvPreviewHost.Opacity = opacity;
    }

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
            "TR" => "Canto superior direito",
            "BL" => "Canto inferior esquerdo",
            "BR" => "Canto inferior direito",
            _ => "Canto superior esquerdo",
        };
        if (_corner != null && _corner != corner && SystemParameters.ClientAreaAnimation)
            OvPreviewHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, OpacityOf(OvOpacity.Value), TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        _corner = corner;
    }

    /// <summary>Atualiza a prévia com os itens marcados. <paramref name="sample"/> = FPS de exemplo (nenhum jogo na frente).</summary>
    internal void ShowPreview(FrameStats f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra, bool sample, bool overlayOn)
    {
        OvPreview.Update(f, lines, show, extra);
        OvSampleNote.Visibility = sample ? Visibility.Visible : Visibility.Collapsed;
        // LED do monitor: aceso no sinal quando o overlay está ligado (paleta fixa, como a cena)
        OvLed.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, overlayOn ? "HudSignal" : "HudSep");
    }
}
