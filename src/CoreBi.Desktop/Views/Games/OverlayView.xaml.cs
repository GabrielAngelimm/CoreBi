using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class OverlayView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public OverlayView() => InitializeComponent();
    private void ChkOverlay_Changed(object sender, RoutedEventArgs e) => Owner?.ChkOverlay_Changed(sender, e);
    private void OvOption_Changed(object sender, RoutedEventArgs e) => Owner?.OvOption_Changed(sender, e);
    private void OvPreset_Click(object sender, RoutedEventArgs e) => Owner?.OvPreset_Click(sender, e);
}
