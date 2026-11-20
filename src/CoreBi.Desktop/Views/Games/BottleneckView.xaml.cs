using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class BottleneckView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public BottleneckView() => InitializeComponent();
    private void BnBalanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Owner?.BnBalanceCanvas_SizeChanged(sender, e);
    private void BtnBnSession_Click(object sender, RoutedEventArgs e) => Owner?.BtnBnSession_Click(sender, e);

    /// <summary>Abaixo de 1200 px o painel de leituras desce para baixo da análise de sessão, na largura toda.</summary>
    private void BnPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = BnPage.ActualWidth < 1200;
        BnSideColumn.Width = new GridLength(narrow ? 0 : 540);
        BnGapColumn.Width = new GridLength(narrow ? 0 : 16);
        BnStackGap.Height = new GridLength(narrow ? 16 : 0);
        Grid.SetColumn(BnSideCard, narrow ? 0 : 2);
        Grid.SetRow(BnSideCard, narrow ? 4 : 0);
        Grid.SetRowSpan(BnSideCard, narrow ? 1 : 3);
    }
}
