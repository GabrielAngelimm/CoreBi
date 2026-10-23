using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class SensorsView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public SensorsView() => InitializeComponent();
    private void SensorSearch_Changed(object sender, TextChangedEventArgs e) => Owner?.SensorSearch_Changed(sender, e);
}
