using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class DisksView : UserControl
{
    private FeatureContext _context = null!;
    private SensorMonitor _monitor = null!;
    private bool _refreshing;
    public DisksView() => InitializeComponent();

    internal void Initialize(FeatureContext context, SensorMonitor monitor)
    {
        _context = context;
        _monitor = monitor;
    }

    private async void BtnDiskRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (_refreshing || _context.IsClosing) return;
        _refreshing = true;
        BtnDiskRefresh.IsEnabled = false;
        DiskHealthStatus.Text = "Lendo os discos…";
        try
        {
            var sensors = _monitor.Snapshot();
            var cards = await Task.Run(() => DiskHealth.Read(sensors));
            if (_context.IsClosing) return;
            DiskCards.ItemsSource = cards;
            int bad = cards.Count(c => c.Verdict is DiskVerdict.Warning or DiskVerdict.Critical);
            int unknown = cards.Count(c => c.Verdict == DiskVerdict.Unknown);
            string summary = bad > 0 ? $"{bad} precisa(m) de atenção"
                : unknown > 0 ? $"{unknown} sem dados suficientes" : "nenhum problema encontrado";
            DiskHealthStatus.Text = cards.Count == 0 ? "Nenhum disco encontrado."
                : $"{cards.Count} disco(s) · {summary} · lido às {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { if (!_context.IsClosing) DiskHealthStatus.Text = "Falhou: " + ex.Message; }
        finally
        {
            _refreshing = false;
            if (!_context.IsClosing) BtnDiskRefresh.IsEnabled = true;
        }
    }
}
