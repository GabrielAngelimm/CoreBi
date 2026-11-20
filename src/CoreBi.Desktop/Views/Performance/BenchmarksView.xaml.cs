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

    /// <summary>
    /// Janelas estreitas: as teclas dos testes passam a duas linhas e a coluna do mostrador do palco encolhe, para a cena e o
    /// traço continuarem legíveis. Com a janela maximizada fica tudo numa linha.
    /// </summary>
    private void View_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        BenchTestPanel.Rows = e.NewSize.Width < 1080 ? 2 : 1;
        var readout = new GridLength(e.NewSize.Width < 1400 ? 200 : 260);
        if (Resources["ReadoutWidth"] is not GridLength current || current != readout) Resources["ReadoutWidth"] = readout;
    }
}
