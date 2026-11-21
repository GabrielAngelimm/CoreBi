using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CoreBi.Desktop;

/// <summary>
/// Navegação entre as páginas: barra lateral, páginas "frias" (véu de carregamento na primeira visita)
/// e a animação de entrada.
/// </summary>
public partial class MainWindow
{
    private static readonly Dictionary<string, string> ColdPages = new()
    {
        [nameof(PageSensors)] = "Lendo os sensores…",
        [nameof(PageDisks)] = "Lendo os discos…",
        [nameof(PageSystem)] = "Lendo as informações do sistema…",
        [nameof(PageEnergy)] = "Lendo bateria e energia…",
        [nameof(PageStartup)] = "Listando os programas de inicialização…",
        [nameof(PageApps)] = "Montando o catálogo de apps…",
    };

    private readonly HashSet<string> _warmPages = new();
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
        bool cold = ColdPages.TryGetValue(page, out string? what) && !_warmPages.Contains(page);

        if (cold)
        {
            LoadingText.Text = what;
            ShowPage(page, deferred: true);
            await Dispatcher.Yield(DispatcherPriority.Background); // deixa o esqueleto ser desenhado antes do trabalho pesado
            if (version != _navVersion || _context.IsClosing) return;
            RevealPage(page);
        }
        else ShowPage(page);

        Task? work = null;
        switch (page)
        {
            case nameof(PageDash): PageDash.UpdateDashboard(); PageDash.RevealRings(); break;
            case nameof(PageOverlay): PageOverlay.UpdatePreview(); break;
            case nameof(PageDisks): work = PageDisks.RefreshAsync(); break;
            case nameof(PageEnergy): work = PageEnergy.RefreshAsync(); break;
            case nameof(PageDriver): PageDriver.InitDriver(); break;
            case nameof(PageStartup): work = PageStartup.RefreshAsync(); break;
            case nameof(PageApps): work = PageApps.RefreshAsync(); break;
        }

        if (!cold)
        {
            if (work != null) await work;
            return;
        }

        // Com os dados já em mãos basta esperar a página montar; senão, no máximo 2,5 s de espera.
        if (work != null) await Task.WhenAny(work, Task.Delay(2500));
        else await Dispatcher.Yield(DispatcherPriority.Background);
        _warmPages.Add(page);
        if (version == _navVersion) HideLoadingVeil(version);
        if (work != null) await work;
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

    internal void ShowPage(string name, bool deferred = false)
    {
        foreach (var p in Pages)
        {
            bool show = !deferred && p.Name == name;
            if (show && p.Visibility != Visibility.Visible) AnimateIn(p);
            p.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        SetLoadingVeil(deferred);
        SideMonitor.Visibility = name == nameof(PageDash) ? Visibility.Hidden : Visibility.Visible; // Hidden (não Collapsed): o rodapé não pula
        SyncFrameMonitoring();
    }

    private void RevealPage(string name)
    {
        foreach (var p in Pages)
            if (p.Name == name) p.Visibility = Visibility.Visible;
    }

    private void SetLoadingVeil(bool on)
    {
        LoadingVeil.BeginAnimation(OpacityProperty, null);
        LoadingBars.BeginAnimation(OpacityProperty, null);
        LoadingVeil.Opacity = 1;
        LoadingVeil.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on && SystemParameters.ClientAreaAnimation)
        {
            LoadingBars.BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
    }

    private void HideLoadingVeil(int version)
    {
        if (LoadingVeil.Visibility != Visibility.Visible) return;
        if (!SystemParameters.ClientAreaAnimation) { SetLoadingVeil(false); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => { if (version == _navVersion) SetLoadingVeil(false); };
        LoadingVeil.BeginAnimation(OpacityProperty, fade);
    }

    private static void AnimateIn(FrameworkElement page)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        if (page.RenderTransform is not TranslateTransform shift) page.RenderTransform = shift = new TranslateTransform();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
    }
}
