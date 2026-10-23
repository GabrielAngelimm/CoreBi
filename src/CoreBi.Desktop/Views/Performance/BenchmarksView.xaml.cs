using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class BenchmarksView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public BenchmarksView() => InitializeComponent();
    private void BenchTest_Checked(object sender, RoutedEventArgs e) => Owner?.BenchTest_Checked(sender, e);
    private void BtnBench_Click(object sender, RoutedEventArgs e) => Owner?.BtnBench_Click(sender, e);
    private void BtnDiskBench_Click(object sender, RoutedEventArgs e) => Owner?.BtnDiskBench_Click(sender, e);
    private void BtnGpuBench_Click(object sender, RoutedEventArgs e) => Owner?.BtnGpuBench_Click(sender, e);
    private void BtnMemBench_Click(object sender, RoutedEventArgs e) => Owner?.BtnMemBench_Click(sender, e);
    private void BtnRtBench_Click(object sender, RoutedEventArgs e) => Owner?.BtnRtBench_Click(sender, e);
    private void BtnSimBench_Click(object sender, RoutedEventArgs e) => Owner?.BtnSimBench_Click(sender, e);
}
