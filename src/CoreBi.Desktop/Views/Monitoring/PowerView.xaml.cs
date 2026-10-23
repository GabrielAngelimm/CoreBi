using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Power;
using CoreBi.Engine.Monitoring.Sensors;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class PowerView : UserControl
{
    private FeatureContext _context = null!;
    private SensorMonitor _monitor = null!;
    private bool _refreshing;
    public PowerView() => InitializeComponent();

    internal void Initialize(FeatureContext context, SensorMonitor monitor)
    {
        _context = context;
        _monitor = monitor;
    }

    private async void BtnEnergyRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (_refreshing || _context.IsClosing) return;
        _refreshing = true;
        BtnEnergyRefresh.IsEnabled = false;
        EnergyStatus.Text = "Lendo bateria e energia…";
        try
        {
            var sensors = _monitor.Snapshot();
            var cards = await Task.Run(() => BatteryInfo.Read(sensors));
            if (_context.IsClosing) return;
            EnergyCards.ItemsSource = cards;
            bool hasBattery = cards.Count > 1;
            EnergyStatus.Text = (hasBattery ? "Bateria detectada" : "Nenhuma bateria (PC de mesa)") + $" · lido às {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { if (!_context.IsClosing) EnergyStatus.Text = "Falhou: " + ex.Message; }
        finally
        {
            _refreshing = false;
            if (!_context.IsClosing) BtnEnergyRefresh.IsEnabled = true;
        }
    }
}
