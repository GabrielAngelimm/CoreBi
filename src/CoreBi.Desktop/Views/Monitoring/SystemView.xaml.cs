using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class SystemView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public SystemView() => InitializeComponent();
    private void BtnReport_Click(object sender, RoutedEventArgs e) => Owner?.BtnReport_Click(sender, e);
    private void InfoCategories_Changed(object sender, SelectionChangedEventArgs e) => Owner?.InfoCategories_Changed(sender, e);
}
