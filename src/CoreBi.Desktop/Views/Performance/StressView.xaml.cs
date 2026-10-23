using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class StressView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public StressView() => InitializeComponent();
    private void BtnStress_Click(object sender, RoutedEventArgs e) => Owner?.BtnStress_Click(sender, e);
    private void ChkGpu_Click(object sender, RoutedEventArgs e) => Owner?.ChkGpu_Click(sender, e);
    private void ChkRam_Click(object sender, RoutedEventArgs e) => Owner?.ChkRam_Click(sender, e);
    private void TempChart_SizeChanged(object sender, SizeChangedEventArgs e) => Owner?.TempChart_SizeChanged(sender, e);
}
