using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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
    private void DashEvents_SizeChanged(object sender, SizeChangedEventArgs e) => Owner?.DashEvents_SizeChanged(sender, e);

    /// <summary>Quatro componentes lado a lado quando há largura (janela maximizada); senão, dois por linha.</summary>
    private void DashComponents_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || FindPanel(DashComponents) is not UniformGrid grid) return;
        grid.Columns = e.NewSize.Width >= 1000 ? 4 : 2;
    }

    private static Panel? FindPanel(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Panel { IsItemsHost: true } panel) return panel;
            if (FindPanel(child) is Panel found) return found;
        }
        return null;
    }
}
