using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class DisksView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public DisksView() => InitializeComponent();
    private void BtnDiskRefresh_Click(object sender, RoutedEventArgs e) => Owner?.BtnDiskRefresh_Click(sender, e);
}
