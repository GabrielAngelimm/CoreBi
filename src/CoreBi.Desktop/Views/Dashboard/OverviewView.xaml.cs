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
    private void DashChart_MouseMove(object sender, MouseEventArgs e) => Owner?.DashChart_MouseMove(sender, e);
    private void DashChart_MouseLeave(object sender, MouseEventArgs e) => Owner?.DashChart_MouseLeave(sender, e);
    private void DashGo_Click(object sender, RoutedEventArgs e) => Owner?.DashGo_Click(sender, e);
    private void DashOption_Checked(object sender, RoutedEventArgs e) => Owner?.DashOption_Checked(sender, e);
    private void DashSensors_Click(object sender, RoutedEventArgs e) => Owner?.DashSensors_Click(sender, e);
    private void DashEventsMore_Click(object sender, RoutedEventArgs e) => DashEventsPopup.IsOpen = true;
    private void DashEvents_SizeChanged(object sender, SizeChangedEventArgs e) => Owner?.DashEvents_SizeChanged(sender, e);

    /// <summary>Com largura (janela maximizada) tudo numa linha; senão, os anéis descem para baixo dos dois cartões.</summary>
    private void DashComponentsRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        bool wide = e.NewSize.Width >= 1000;
        Grid.SetRow(DashMemoryCard, wide ? 0 : 1);
        Grid.SetColumn(DashMemoryCard, wide ? 1 : 0);
        Grid.SetColumnSpan(DashMemoryCard, wide ? 1 : 2);
        DashMemoryCard.Margin = new Thickness(0, wide ? 0 : 16, 0, 0);
        Grid.SetColumnSpan(DashComponents, wide ? 1 : 2);
        DashComponents.Margin = new Thickness(0, 0, wide ? 0 : -16, 0);
    }
}
