using System.Runtime.InteropServices;
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
using System.Windows.Media.Animation;

using CoreBi.Desktop.Services;

namespace CoreBi.Desktop;

public partial class MainWindow : Window
{
    internal readonly WorkloadCoordinator _workloads = new();
    internal IDisposable? _stressLease;
    internal readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FeatureContext _features;

    internal readonly AppSettings _settings = AppSettings.Load();

    /// <summary>
    /// Pincel atual da paleta, para o que é desenhado em código (gráficos), refeito na troca de tema.
    /// Estados de controles usam SetResourceReference, que acompanha o tema sozinho.
    /// </summary>
    internal static SolidColorBrush Palette(string key) => ThemeManager.Brush(key);

    public MainWindow()
    {
        InitializeComponent();
        _monitor = new SensorMonitor();
        _features = new FeatureContext(this, _workloads, _lifetime.Token);
        ConnectViews();
        ThemeManager.Apply(_settings.LightTheme);
        UpdateThemeButton();
        BuildPalette();
        ShowPage("PageDash");

        // Sensores: a lista começa vazia e é preenchida quando a detecção do hardware termina (LoadHardwareAsync).
        _view = CollectionViewSource.GetDefaultView(_monitor.Rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorRow.Hardware)));
        SensorGrid.ItemsSource = _view;
        SystemSub.Text = "Lendo informações do sistema…";
        StatusText.Text = "Detectando o hardware…";
        SideVersion.Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1";

        InitBench();
        SnapshotUsb();
        RefreshDrives();
        UpdateDashboard();

        // A janela aparece já; as coletas pesadas (WMI, sensores, GPU) rodam em paralelo fora da thread da janela.
        Loaded += async (_, _) => await LoadHardwareAsync();
        SourceInitialized += (_, _) =>
        {
            RoundCorners();
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(MaximizeHook);
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            // Durante a cena de GPU as leituras de sensores (NVML/driver) ficam pausadas: cada consulta trava o driver
            // por um instante e aparecia como engasgo no 1% low do benchmark.
            if (_gpuBenchActive) { UpdateStressPanel(); return; }
            _monitor.Refresh();
            if (_monitor.IsOpen) StatusText.Text = $"{_monitor.Rows.Count} sensores · atualizado às {DateTime.Now:HH:mm:ss}";
            UpdateStressPanel();
            UpdateGpuLive();
            UpdateSideStorage();
            UpdateCpuSensorWarning();
            CollectDashSample();
            PageDisks.Sample();
            UpdateFrameMonitoring();
            UpdateBottleneck();
            if (PageBench.IsVisible) UpdateBenchLive();
            UpdateOverlayPreview();
        };
        _timer.Start();

        // Abre maximizada; Restaurar volta ao tamanho padrão (1360 × 860), centralizada.
        StateChanged += (_, _) => UpdateMaximizeChrome();
        WindowState = WindowState.Maximized;
        UpdateMaximizeChrome();
        Closing += (_, e) =>
        {
            if (_features.HasCriticalOperation)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Aguarde a instalação terminar. Em Instalar apps, Parar encerra a fila depois do app atual.", "CoreBi", MessageBoxButton.OK, MessageBoxImage.Information);
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
            _rtssOverlay.Dispose();
            _toast?.Close();
            _frames.Dispose();
            _windowsGpuUsage.Dispose();
            _gameFetch?.Cancel();
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
                SystemSub.Text = $"{Environment.MachineName} · {info.Count} categorias de hardware e software";
            }
            catch (Exception ex) { SystemSub.Text = $"Não foi possível ler as informações do sistema: {ex.Message}"; }
        }

        async Task LoadSensors()
        {
            try { await _monitor.OpenAsync(); }
            catch (Exception ex)
            {
                StatusText.Text = $"Sensores indisponíveis: {ex.Message}";
                return;
            }
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
                var chip = new RadioButton { Content = GameCatalogShort(g.Name), ToolTip = g.Name, GroupName = "Gpu", Style = (Style)FindResource("Chip"), IsChecked = i == 0 };
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

    private static string GameCatalogShort(string gpu) => CoreBi.Engine.Games.GameCatalog.ShortGpuName(gpu);

    // ---------------- Janela / navegação ----------------

    internal void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    internal void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    internal void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Maximizada, a janela ocupa exatamente o espaço de trabalho (MaximizeHook) e dispensa o contorno.</summary>
    private void UpdateMaximizeChrome()
    {
        bool max = WindowState == WindowState.Maximized;
        Root.BorderThickness = max ? new Thickness(0) : new Thickness(1);
        BtnMax.Content = max ? "" : "";
        BtnMax.ToolTip = max ? "Restaurar" : "Maximizar";
    }

    /// <summary>Páginas pesadas: na primeira visita mostram o esqueleto enquanto montam e leem os dados.</summary>
    private static readonly Dictionary<string, string> ColdPages = new()
    {
        ["PageSensors"] = "Lendo os sensores…",
        ["PageDisks"] = "Lendo os discos…",
        ["PageSystem"] = "Lendo as informações do sistema…",
        ["PageEnergy"] = "Lendo bateria e energia…",
        ["PageStartup"] = "Listando os programas de inicialização…",
        ["PageApps"] = "Montando o catálogo de apps…",
    };
    private readonly HashSet<string> _warmPages = new();
    private int _navVersion;

    /// <summary>
    /// A troca de página é imediata: o item do menu e o esqueleto aparecem no primeiro quadro; só depois a página pesada
    /// é montada (por baixo do esqueleto) e os dados são lidos em segundo plano. Nas visitas seguintes não há esqueleto.
    /// </summary>
    internal async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageSystem == null) return; // disparado durante InitializeComponent
        string page = (string)((FrameworkElement)sender).Tag;
        int version = ++_navVersion;
        bool cold = ColdPages.TryGetValue(page, out string? what) && !_warmPages.Contains(page);

        if (cold)
        {
            LoadingText.Text = what;
            ShowPage(page, deferred: true);
            await Dispatcher.Yield(DispatcherPriority.Background); // deixa o esqueleto ser desenhado antes do trabalho pesado
            if (version != _navVersion || _features.IsClosing) return;
            RevealPage(page);
        }
        else ShowPage(page);

        // Algumas páginas carregam dados só quando abertas.
        Task? work = null;
        switch (page)
        {
            case "PageDash": UpdateDashboard(); break;
            case "PageOverlay": UpdateOverlayPreview(); break;
            case "PageDisks": work = PageDisks.RefreshAsync(); break;
            case "PageEnergy": work = PageEnergy.RefreshAsync(); break;
            case "PageDriver": PageDriver.InitDriver(); break;
            case "PageStartup": work = PageStartup.ScanStartupAsync(); break;
            case "PageApps": work = PageApps.InitAppsAsync(); break;
        }

        if (!cold)
        {
            if (work != null) await work;
            return;
        }

        // Com os dados já em mãos (Sensores, Sistema) basta esperar a página montar; senão, no máximo 2,5 s de espera.
        if (work != null) await Task.WhenAny(work, Task.Delay(2500));
        else await Dispatcher.Yield(DispatcherPriority.Background);
        _warmPages.Add(page);
        if (version == _navVersion) HideLoadingVeil(version);
        if (work != null) await work;
    }

    private FrameworkElement[] Pages => new FrameworkElement[] { PageDash, PageSystem, PageSensors, PageGpu, PageGames, PageBottleneck, PageOverlay, PageBench, PageDriver, PageStartup, PageApps, PageDisks, PageEnergy, PageNet, PageStress };

    /// <summary>Com deferred, nenhuma página fica visível: só o esqueleto, até RevealPage.</summary>
    internal void ShowPage(string name, bool deferred = false)
    {
        foreach (var p in Pages)
        {
            bool show = !deferred && p.Name == name;
            if (show && p.Visibility != Visibility.Visible) AnimateIn(p);
            p.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        SetLoadingVeil(deferred);
        SideMonitor.Visibility = name == "PageDash" ? Visibility.Collapsed : Visibility.Visible;
        UpdateFrameMonitoring();
    }

    /// <summary>Monta a página por baixo do esqueleto (o custo da primeira exibição fica coberto por ele).</summary>
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
        // Pulso leve só enquanto o esqueleto está na tela (nada roda depois que a página abre).
        if (on && SystemParameters.ClientAreaAnimation)
            LoadingBars.BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(900))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }

    private void HideLoadingVeil(int version)
    {
        if (LoadingVeil.Visibility != Visibility.Visible) return;
        if (!SystemParameters.ClientAreaAnimation) { SetLoadingVeil(false); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => { if (version == _navVersion) SetLoadingVeil(false); };
        LoadingVeil.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Central da barra lateral: RAM em uso e ocupação do disco do sistema (CPU e GPU vêm do teste de carga e da GPU).</summary>
    internal void UpdateSideStorage()
    {
        var (total, avail) = CoreBi.Engine.Monitoring.Memory.MemoryInfo.MemoryStatus();
        if (total > 0)
        {
            double used = (double)(total - avail) / total;
            SideRam.Text = $"{(total - avail) / 1073741824.0:0.0} GB";
            SideRamLoad.Text = $"{used * 100:0} %";
            SideRamBar.ScaleX = Math.Clamp(used, 0, 1);
        }
        try
        {
            var c = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            if (!c.IsReady || c.TotalSize <= 0) return;
            double used = (double)(c.TotalSize - c.TotalFreeSpace) / c.TotalSize;
            SideDisk.Text = CoreBi.Engine.Monitoring.Storage.DriveCard.Fmt(c.TotalFreeSpace) + " livres";
            SideDiskLoad.Text = $"{used * 100:0} %";
            SideDiskBar.ScaleX = Math.Clamp(used, 0, 1);
            SideDiskRow.ToolTip = $"Disco {c.Name.TrimEnd('\\')}: {CoreBi.Engine.Monitoring.Storage.DriveCard.Fmt(c.TotalSize - c.TotalFreeSpace)} usados de {CoreBi.Engine.Monitoring.Storage.DriveCard.Fmt(c.TotalSize)}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Entrada da página: resposta curta à navegação (respeita "mostrar animações" do Windows).</summary>
    private static void AnimateIn(FrameworkElement page)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        if (page.RenderTransform is not TranslateTransform shift) page.RenderTransform = shift = new TranslateTransform();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
    }

    internal bool SaveSettings()
    {
        try { _settings.Save(); return true; }
        catch (Exception ex)
        {
            CoreBi.Engine.Infrastructure.DiagnosticLog.Write("settings.save", ex);
            Toast("Não foi possível salvar", "A configuração atual não foi gravada. Confira o espaço e as permissões da pasta de dados.", "");
            return false;
        }
    }

    /// <summary>Cantos arredondados do Windows 11 para a janela sem moldura nativa (ignorado no Windows 10).</summary>
    private void RoundCorners()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Sem moldura nativa, o Windows maximiza a janela no tamanho do monitor inteiro (mais a borda de redimensionar),
    /// e o rodapé de todas as páginas ficava atrás da barra de tarefas. WM_GETMINMAXINFO limita ao espaço de trabalho
    /// do monitor em que a janela está.
    /// </summary>
    private static IntPtr MaximizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        IntPtr monitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        mmi.MaxPosition = new NativePoint(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
        mmi.MaxSize = new NativePoint(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        Marshal.StructureToPtr(mmi, lParam, false);
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
