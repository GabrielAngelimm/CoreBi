using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Performance;

using CoreBi.Engine.Persistence;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

using CoreBi.Desktop.Services;

namespace CoreBi.Desktop;

public partial class MainWindow : Window
{
    internal readonly WorkloadCoordinator _workloads = new();
    internal IDisposable? _stressLease;
    internal readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FeatureContext _features;

    // Jogos
    internal readonly AppSettings _settings = AppSettings.Load();

    internal Brush Fg => (Brush)FindResource("Fg");
    internal Brush Ink => (Brush)FindResource("Ink");
    internal Brush Muted => (Brush)FindResource("Muted");
    internal Brush Card => (Brush)FindResource("Card");

    public MainWindow()
    {
        InitializeComponent();
        _features = new FeatureContext(this, _workloads, _lifetime.Token);
        ConnectViews();
        ThemeManager.Apply(_settings.LightTheme);
        UpdateThemeButton();
        ShowPage("PageDash");

        // Sensores: a lista começa vazia e é preenchida quando a detecção do hardware termina (LoadHardwareAsync).
        _monitor = new SensorMonitor();
        _view = CollectionViewSource.GetDefaultView(_monitor.Rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorRow.Hardware)));
        SensorGrid.ItemsSource = _view;
        SystemSub.Text = "Lendo informações do sistema…";
        StatusText.Text = "Detectando o hardware…";

        InitBench();
        SnapshotUsb();
        RefreshDrives();
        TitlePage.Text = "Visão geral";
        UpdateDashboard();

        // A janela aparece já; as coletas pesadas (WMI, sensores, GPU) rodam em paralelo fora da thread da janela.
        Loaded += async (_, _) => await LoadHardwareAsync();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            // Durante a cena de GPU as leituras de sensores (NVML/driver) ficam pausadas: cada consulta trava o driver
            // por um instante e aparecia como engasgo no 1% low do benchmark.
            if (_gpuBenchActive) { UpdateStressPanel(); return; }
            _monitor.Refresh();
            if (_monitor.IsOpen) StatusText.Text = $"{_monitor.Rows.Count} sensores · atualizado {DateTime.Now:HH:mm:ss}";
            UpdateStressPanel();
            UpdateGpuLive();
            UpdateCpuSensorWarning();
            CollectDashSample();
            UpdateFrameMonitoring();
            UpdateBottleneck();
            if (PageBench.IsVisible) UpdateBenchLive();
        };
        _timer.Start();

        StateChanged += (_, _) =>
        {
            // Sem borda nativa, a janela maximizada vaza ~7 px para fora da tela.
            bool max = WindowState == WindowState.Maximized;
            Root.Margin = max ? new Thickness(7) : new Thickness(0);
            BtnMax.Content = max ? "" : "";
        };
        Closing += (_, e) =>
        {
            if (_features.HasCriticalOperation)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Aguarde a instalação terminar. Em Instalar apps, PARAR encerra a fila depois do app atual.", "CoreBi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _lifetime.Cancel();
        };
        Closed += (_, _) =>
        {
            _timer.Stop(); _overlayTimer?.Stop(); _usbDebounce?.Stop();
            _stress.Stop(); _burn.Stop(); _benchCts?.Cancel();
            _gameBenchCts?.Cancel(); _gpuBench?.Cancel(); _rtBench?.Cancel(); _stressLease?.Dispose();
            _overlay?.Close();
            _toast?.Close();
            _frames.Dispose();
            _gameFetch?.Cancel(); _netCts?.Cancel();
            UnregisterHotKey(new System.Windows.Interop.WindowInteropHelper(this).Handle, HotkeyId);
            _monitor.Dispose();
        };
    }

    /// <summary>Coletas lentas da abertura, em paralelo e fora da thread da janela; cada parte preenche a tela quando fica pronta.</summary>
    internal async Task LoadHardwareAsync()
    {
        async Task LoadInfo()
        {
            try
            {
                var info = await Task.Run(SystemInfoCollector.Collect);
                if (_features.IsClosing) return;
                InfoCategories.ItemsSource = info;
                InfoCategories.SelectedIndex = 0;
                SystemSub.Text = $"{Environment.MachineName} · {info.Count} categorias";
            }
            catch (Exception ex) { SystemSub.Text = $"Não foi possível ler as informações do sistema: {ex.Message}"; }
        }

        async Task LoadSensors()
        {
            try { await _monitor.OpenAsync(); }
            catch (Exception ex) { StatusText.Text = $"Sensores indisponíveis: {ex.Message}"; return; }
            if (_features.IsClosing) return;
            _monitor.Refresh();
            UpdateCpuSensorWarning();
            AddChip("Todos", null, isChecked: true);
            foreach (var c in _monitor.Rows.Select(r => r.Category).Distinct().OrderBy(x => x))
                AddChip(c, c);
            UpdateDashboard();
        }

        async Task LoadGpu()
        {
            try { _gpus = await Task.Run(GpuInfoCollector.Collect); }
            catch { _gpus = new(); }
            if (_features.IsClosing) return;
            for (int i = 0; i < _gpus.Count && _gpus.Count > 1; i++)
            {
                var g = _gpus[i];
                var chip = new RadioButton { Content = g.Name, GroupName = "Gpu", Style = (Style)FindResource("Chip"), IsChecked = i == 0 };
                chip.Checked += (_, _) => ShowGpu(g);
                GpuChips.Children.Add(chip);
            }
            if (_gpus.Count > 0) ShowGpu(_gpus[0]);

            // Dependem do nome da GPU/CPU
            InitGames();
            InitBottleneckAndOverlay();
            UpdateDashboard();
        }

        await Task.WhenAll(LoadInfo(), LoadSensors(), LoadGpu());
    }

    // ---------------- Janela / navegação ----------------

    internal void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    internal void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    internal void Close_Click(object sender, RoutedEventArgs e) => Close();

    internal async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageSystem == null) return; // disparado durante InitializeComponent
        var item = (FrameworkElement)sender;
        string page = (string)item.Tag;
        ShowPage(page);

        // Abre a categoria da página (ex.: quando o painel pula direto para o Benchmark)
        // e mostra "CATEGORIA · página" na barra de título.
        string name = item is ContentControl { Content: Panel panel }
            ? panel.Children.OfType<TextBlock>().LastOrDefault()?.Text ?? "" : "";
        if (item.Parent is FrameworkElement { Name: var items } && items.EndsWith("Items")
            && FindName(items[..^5]) is System.Windows.Controls.Primitives.ToggleButton group)
        {
            group.IsChecked = true;
            name = $"{group.Content} · {name}";
        }
        TitlePage.Text = name;

        // Algumas páginas carregam dados só quando abertas.
        switch (page)
        {
            case "PageDash": UpdateDashboard(); break;
            case "PageDisks": RefreshDiskHealth(); break;
            case "PageEnergy": RefreshEnergy(); break;
            case "PageDriver": PageDriver.InitDriver(); break;
            case "PageStartup": await PageStartup.ScanStartupAsync(); break;
            case "PageApps": await PageApps.InitAppsAsync(); break;
        }
    }

    internal void ShowPage(string name)
    {
        foreach (var p in new FrameworkElement[] { PageDash, PageSystem, PageSensors, PageGpu, PageGames, PageBottleneck, PageOverlay, PageBench, PageDriver, PageStartup, PageApps, PageDisks, PageEnergy, PageNet, PageStress })
            p.Visibility = p.Name == name ? Visibility.Visible : Visibility.Collapsed;
        UpdateFrameMonitoring();
    }

    internal bool RequireAdmin()
    {
        return PermissionGuard.RequireAdmin(this);
    }

    internal bool SaveSettings()
    {
        try { _settings.Save(); return true; }
        catch (Exception ex)
        {
            CoreBi.Engine.Infrastructure.DiagnosticLog.Write("settings.save", ex);
            Toast("Não foi possível salvar", "A configuração atual não foi gravada. Confira o espaço e as permissões da pasta de dados.", "\uE783");
            return false;
        }
    }

}
