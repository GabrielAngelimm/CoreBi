using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CoreBi.Desktop;

/// <summary>
/// Navegação entre as páginas: barra lateral e a entrada da página. Toda página aparece no instante do clique, com a mesma
/// entrada (PageMotion); o que ainda precisa ser lido ou montado aparece como esqueleto nos painéis (Skeleton) e entra quando
/// fica pronto, sem segurar a troca.
/// </summary>
public partial class MainWindow
{
    private int _navVersion;

    internal void GoToPage(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        var parts = target.Split(':', 2);
        string pageName = parts[0];
        var nav = NavPanel.Children.OfType<RadioButton>().FirstOrDefault(rb => (string)rb.Tag == pageName);
        if (nav != null) nav.IsChecked = true;

        if (parts.Length < 2) return;
        switch (pageName)
        {
            case nameof(PageSensors): PageSensors.ShowComponent(parts[1]); break;
            case nameof(PageSystem): PageSystem.Select(parts[1]); break;
            case nameof(PageBench): PageBench.SelectTest(parts[1]); break;
        }
    }

    private async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageSystem == null) return; // disparado durante InitializeComponent
        KeepActiveNavVisible();
        string page = (string)((FrameworkElement)sender).Tag;
        int version = ++_navVersion;
        ShowPage(page);
        if (page == nameof(PageDash)) PageDash.RevealRings(); // os anéis enchem do zero junto com a entrada

        // A leitura da página começa depois do primeiro quadro dela: a troca nunca espera os dados. Ao voltar a uma página já lida,
        // a releitura é silenciosa (sem "Lendo…", medidor nem botões apagando) e só troca o que mudou, depois da entrada
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (version != _navVersion || _context.IsClosing) return;
        switch (page)
        {
            case nameof(PageDash): PageDash.UpdateDashboard(); break;
            case nameof(PageOverlay): PageOverlay.UpdatePreview(); break;
            case nameof(PageDisks): await PageDisks.RefreshAsync(entering: true); break;
            case nameof(PageEnergy): await PageEnergy.RefreshAsync(entering: true); break;
            case nameof(PageDriver): PageDriver.InitDriver(); break;
            case nameof(PageStartup): await PageStartup.RefreshAsync(entering: true); break;
            case nameof(PageApps): await PageApps.RefreshAsync(entering: true); break;
        }
    }

    private void KeepActiveNavVisible() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            NavPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.BringIntoView());

    private void NavScroll_SizeChanged(object sender, SizeChangedEventArgs e) => KeepActiveNavVisible();

    private FrameworkElement[] Pages => new FrameworkElement[]
    {
        PageDash, PageSystem, PageSensors, PageGpu, PageBottleneck, PageOverlay,
        PageBench, PageDriver, PageStartup, PageApps, PageDisks, PageEnergy, PageNet, PageStress,
    };

    internal void ShowPage(string name)
    {
        foreach (var p in Pages)
        {
            bool show = p.Name == name;
            if (show == (p.Visibility == Visibility.Visible)) continue;
            if (show) PageMotion.Enter(p); // antes de mostrar: quem reage a ficar visível já sabe que a entrada começou
            p.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        SideMonitor.Visibility = name == nameof(PageDash) ? Visibility.Hidden : Visibility.Visible; // Hidden (não Collapsed): o rodapé não pula
        SyncFrameMonitoring();
    }
}
