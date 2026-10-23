using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class OverviewView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public OverviewView() => InitializeComponent();
    private void DashBench_Click(object sender, RoutedEventArgs e) => Owner?.DashBench_Click(sender, e);
    private void DashChart_SizeChanged(object sender, SizeChangedEventArgs e) => Owner?.DashChart_SizeChanged(sender, e);
    private void DashGo_Click(object sender, RoutedEventArgs e) => Owner?.DashGo_Click(sender, e);
    private void DashOption_Checked(object sender, RoutedEventArgs e) => Owner?.DashOption_Checked(sender, e);
    private void DashSensors_Click(object sender, RoutedEventArgs e) => Owner?.DashSensors_Click(sender, e);
}
