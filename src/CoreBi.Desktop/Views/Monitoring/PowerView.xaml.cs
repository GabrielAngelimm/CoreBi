using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class PowerView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public PowerView() => InitializeComponent();
    private void BtnEnergyRefresh_Click(object sender, RoutedEventArgs e) => Owner?.BtnEnergyRefresh_Click(sender, e);
}
