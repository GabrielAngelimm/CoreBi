using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Engine.Monitoring.Inventory;

using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    // ---------------- Sistema ----------------

    internal void InfoCategories_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (InfoCategories.SelectedItem is not InfoNode node) return;
        InfoTitle.Text = node.Header;
        InfoDetails.ItemsSource = node.Children;
        bool storage = node.Header == "Armazenamento";
        DrivesPanel.Visibility = storage ? Visibility.Visible : Visibility.Collapsed;
        if (storage) RefreshDrives();
    }

}
