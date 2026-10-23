using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Network;
using CoreBi.Engine.Monitoring.Storage;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class NetworkView : UserControl
{
    internal FeatureContext Context { private get; set; } = null!;
    internal IReadOnlyList<DiskHealthCard>? LastResult { get; private set; }
    private CancellationTokenSource? _cancellation;
    private bool _running;
    public NetworkView() => InitializeComponent();
    private async void BtnNetTest_Click(object sender, RoutedEventArgs e) => await RunOrCancelAsync();

    internal async Task RunOrCancelAsync()
    {
        if (Context.IsClosing) return;
        if (_running) { _cancellation?.Cancel(); return; }
        using var workload = Context.Workloads.TryBegin("Network test");
        if (workload == null) { NetStatus.Text = "Termine o outro teste antes."; return; }

        _running = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Context.Lifetime);
        _cancellation = cancellation;
        BtnNetTest.Content = "CANCELAR";
        NetCards.ItemsSource = null;
        NetProgress.ScaleX = 0;
        try
        {
            var cards = await NetworkTest.Run((text, fraction) =>
            {
                if (Context.IsClosing) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Context.IsClosing) return;
                    NetStatus.Text = text;
                    NetProgress.ScaleX = Math.Clamp(fraction, 0, 1);
                }));
            }, cancellation.Token);
            if (Context.IsClosing) return;
            LastResult = cards;
            NetCards.ItemsSource = cards;
            NetStatus.Text = $"Concluído às {DateTime.Now:HH:mm:ss}. O resultado entra no relatório exportado.";
            NetProgress.ScaleX = 1;
        }
        catch (OperationCanceledException)
        {
            if (!Context.IsClosing) { NetStatus.Text = "Teste cancelado."; NetProgress.ScaleX = 0; }
        }
        catch (Exception ex)
        {
            if (!Context.IsClosing) { NetStatus.Text = "Falhou: " + ex.Message; NetProgress.ScaleX = 0; }
        }
        finally
        {
            _running = false;
            _cancellation = null;
            if (!Context.IsClosing) BtnNetTest.Content = "TESTAR";
        }
    }
}
