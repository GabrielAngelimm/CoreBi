using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class ForecastView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public ForecastView() => InitializeComponent();
    private void ApiKeyBox_Changed(object sender, RoutedEventArgs e) => Owner?.ApiKeyBox_Changed(sender, e);
    private void BtnApiKey_Click(object sender, RoutedEventArgs e) => Owner?.BtnApiKey_Click(sender, e);
    private void GameOption_Changed(object sender, RoutedEventArgs e) => Owner?.GameOption_Changed(sender, e);
    private void GameSearch_Changed(object sender, TextChangedEventArgs e) => Owner?.GameSearch_Changed(sender, e);
}
