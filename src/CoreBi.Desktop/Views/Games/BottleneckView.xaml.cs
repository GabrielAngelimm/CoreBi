using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class BottleneckView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public BottleneckView() => InitializeComponent();
    private void BnBalanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Owner?.BnBalanceCanvas_SizeChanged(sender, e);
    private void BtnBnSession_Click(object sender, RoutedEventArgs e) => Owner?.BtnBnSession_Click(sender, e);
}
