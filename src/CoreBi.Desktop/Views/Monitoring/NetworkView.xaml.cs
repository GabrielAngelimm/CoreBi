using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class NetworkView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public NetworkView() => InitializeComponent();
    private void BtnNetTest_Click(object sender, RoutedEventArgs e) => Owner?.BtnNetTest_Click(sender, e);
}
