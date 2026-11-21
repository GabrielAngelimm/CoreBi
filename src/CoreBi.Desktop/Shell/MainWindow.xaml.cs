using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Persistence;
using CoreBi.Engine.Tools.Apps;

namespace CoreBi.Desktop;

public partial class MainWindow : Window, ILiveMotionHost
{
    /// <summary>Altura de uma linha da atividade recente na Visão geral (usada pelo binding XAML em OverviewView).</summary>
    public const double DashEventHeight = Views.OverviewView.EventRowHeight;

    private const int HotkeyId = 0x5245; // "RE"

    private static readonly Dictionary<string, string> ColdPages = new()
    {
        ["PageSensors"] = "Lendo os sensores…",
        ["PageDisks"] = "Lendo os discos…",
        ["PageSystem"] = "Lendo as informações do sistema…",
        ["PageEnergy"] = "Lendo bateria e energia…",
        ["PageStartup"] = "Listando os programas de inicialização…",
        ["PageApps"] = "Montando o catálogo de apps…",
    };

    internal readonly WorkloadCoordinator _workloads = new();
    internal readonly AppSettings _settings = AppSettings.Load();
    internal readonly SensorMonitor _monitor = new();
    internal readonly FeatureContext _context;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly FrameMonitor _frames = new();
    private readonly GpuMonitor _gpuMonitor = new();
    private readonly HardwareDeviceWatcher _deviceWatcher;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _warmPages = new();
    private ToastWindow? _toast;
    private int _navVersion;
    private int _sideDiskTicks;
    private bool _sideDiskReading;
    private bool _gpuBenchActive;

    public MainWindow()
    {
        InitializeComponent();
        _context = new FeatureContext(
            owner: this,
            workloads: _workloads,
            lifetime: _lifetime.Token,
            settings: _settings,
            sensors: _monitor,
            frames: _frames,
            gpuTelemetry: _gpuMonitor,
            navigate: GoToPage,
            recordEvent: (icon, title, detail, value, channel) => PageDash.AddEvent(icon, title, detail, value, channel),
            notify: ShowToast,
            saveSettings: SaveSettings,
            syncFrameMonitoring: SyncFrameMonitoring,
            setBenchmarkIsolation: SetBenchmarkIsolation,
            isLiveMotion: () => LiveMotion);

        _deviceWatcher = new HardwareDeviceWatcher(_context, Dispatcher, OnStorageChangedAsync);

        ConnectViews();
        ThemeManager.Apply(_settings.LightTheme);
        UpdateThemeButton();
        BuildPalette();
        ShowPage("PageDash");

        SideVersion.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1";

        _deviceWatcher.StartInitialSnapshots();
        RefreshSideStorageAsync();

        Loaded += async (_, _) => await LoadHardwareAsync();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTelemetryTick;
        _timer.Start();

        StateChanged += (_, _) => UpdateMaximizeChrome();
        WindowState = WindowState.Maximized;
        UpdateMaximizeChrome();

        Closing += (_, e) =>
        {
            if (_context.HasCriticalOperation)
            {
                e.Cancel = true;
                MessageBox.Show(this, _context.CriticalOperationMessage, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _lifetime.Cancel();
        };

        Closed += (_, _) =>
        {
            _timer.Stop();
            PageOverlay.Dispose();
            PageBench.Dispose();
            PageStress.Dispose();
            _deviceWatcher.Dispose();
            _toast?.Close();
            _frames.Dispose();
            _gpuMonitor.Dispose();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Win32Window.UnregisterHotKey(hwnd, HotkeyId);
            _monitor.Dispose();
            _lifetime.Dispose();
        };
    }

    bool ILiveMotionHost.LiveMotion => LiveMotion;

    /// <summary>
    /// Movimento que acompanha as leituras só roda com a janela ativa e sem benchmark ou teste de carga:
    /// o CoreBi fica aberto durante jogos e testes e não pode pesar na medição. Respeita animações do Windows.
    /// </summary>
    internal bool LiveMotion => SystemParameters.ClientAreaAnimation && IsActive && !_workloads.IsBusy && !PageBench.IsRunning && !PageStress.IsRunning;

    private void ConnectViews()
    {
        PageDash.Initialize(_context);
        PageSystem.Initialize(_context, () => PageNet.LastResult);
        PageSensors.Initialize(_context);
        PageGpu.Initialize(_context);
        PageDisks.Initialize(_context);
        PageEnergy.Initialize(_context);
        PageNet.Initialize(_context);
        PageOverlay.Initialize(_context);
        PageBottleneck.Initialize(_context);
        PageBench.Initialize(_context);
        PageStress.Initialize(_context);
        PageStartup.Initialize(_context);
        PageApps.Initialize(_context);
        PageDriver.Initialize(_context);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32Window.TryRoundCorners(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        // Ctrl + Shift + O, sem repetição ao segurar (MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT)
        Win32Window.RegisterHotKey(hwnd, HotkeyId, 0x0002 | 0x0004 | 0x4000, 0x4F);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Window.WmGetMinMaxInfo)
            return Win32Window.HandleMaximizeMinMax(hwnd, msg, lParam);
        if (msg == Win32Window.WmHotKey && wParam.ToInt32() == HotkeyId)
        {
            PageOverlay.ToggleOverlay();
            handled = true;
        }
        else if (msg == Win32Window.WmDeviceChange)
        {
            _deviceWatcher.HandleDeviceChange(wParam.ToInt32(), lParam);
        }
        return IntPtr.Zero;
    }

    private void OnTelemetryTick(object? sender, EventArgs e)
    {
        // Durante benchmarks de GPU/disco as leituras de sensores (NVML/driver) ficam pausadas para não gerar engasgos no 1% low.
        if (_gpuBenchActive)
        {
            UpdateSidebarCpu();
            PageStress.Tick();
            return;
        }

        _monitor.Refresh();
        _gpuMonitor.UpdateLoads(_context.Gpus);
        PageSensors.Tick();
        UpdateSidebarTelemetry();
        PageStress.Tick();
        PageGpu.TickLive();
        UpdateCpuSensorWarning();
        PageDash.CollectSample();
        PageDisks.Sample();
        PageEnergy.Sample();
        SyncFrameMonitoring();
        PageBottleneck.Tick();
        if (PageBench.IsVisible) PageBench.TickLive();
        PageOverlay.UpdatePreview();
    }

    internal async Task LoadHardwareAsync()
    {
        Task LoadInfo() => PageSystem.RefreshAsync();

        async Task LoadSensors()
        {
            try { await _monitor.OpenAsync(); }
            catch (Exception ex)
            {
                PageSensors.SetStatus($"Sensores indisponíveis: {ex.Message}");
                return;
            }
            if (_context.IsClosing) return;
            _monitor.Refresh();
            UpdateCpuSensorWarning();
            PageSensors.Populate();
            PageDash.UpdateDashboard();
        }

        async Task LoadGpuAndCpu()
        {
            var gpuTask = Task.Run(GpuInfoCollector.Collect);
            var cpuTask = Task.Run(SystemInfoCollector.ReadCpuName);
            // Falhas independentes: uma placa de vídeo ilegível não apaga o nome do processador.
            List<GpuInfo> gpus;
            try { gpus = await gpuTask; }
            catch { gpus = new(); }
            string cpuName;
            try { cpuName = await cpuTask; }
            catch { cpuName = ""; }
            if (_context.IsClosing) return;

            _context.Gpus = gpus;
            _context.CpuName = cpuName;
            PageGpu.PopulateGpus(gpus);
            PageSystem.GpusReady();
            PageBottleneck.RefreshHardware();
            PageOverlay.RefreshHardware();
            PageBench.RefreshHardware();
            PageDash.UpdateDashboard();
        }

        await Task.WhenAll(LoadInfo(), LoadSensors(), LoadGpuAndCpu());
    }

    private async Task OnStorageChangedAsync()
    {
        RefreshSideStorageAsync();
        await PageSystem.RefreshStorageAsync();
        if (PageDisks.IsVisible) await PageDisks.RefreshAsync();
        if (PageBench.IsVisible) PageBench.RefreshHardware();
        if (PageDash.IsVisible) PageDash.UpdateDashboard(full: false);
    }


    private void UpdateSidebarTelemetry()
    {
        UpdateSidebarCpu();
        if (_context.PrimaryGpu != null)
        {
            float? load = _context.PrimaryGpuLoad();
            SideGpuTemp.Text = _context.PrimaryGpuTemp() is float t ? $"{t:0.#} °C" : "—";
            SideGpuLoad.Text = load is float l ? $"{l:0} %" : "—";
            Ui.Glide(SideGpuBar, (load ?? 0) / 100.0, LiveMotion);
        }

        if (MemoryInfo.ReadPhysicalUsage() is { Total: > 0 } mem)
        {
            double used = (double)mem.Used / mem.Total;
            SideRam.Text = $"{mem.Used / 1073741824.0:0.0} GB";
            SideRamLoad.Text = $"{used * 100:0} %";
            Ui.Glide(SideRamBar, used, LiveMotion);
        }

        if (++_sideDiskTicks >= 5)
        {
            _sideDiskTicks = 0;
            RefreshSideStorageAsync();
        }
    }

    private void UpdateSidebarCpu()
    {
        float? temp = _monitor.CpuTemperature;
        float? load = _monitor.CpuLoad;
        SideTemp.Text = temp == null ? "—" : $"{temp:0.#} °C";
        SideLoad.Text = load == null ? "—" : $"{load:0} %";
        Ui.Glide(SideCpuBar, (load ?? 0) / 100.0, LiveMotion);
    }

    private async void RefreshSideStorageAsync()
    {
        if (_sideDiskReading || _context.IsClosing) return;
        _sideDiskReading = true;
        try
        {
            var stats = await Task.Run(ReadSystemDriveUsage);
            if (_context.IsClosing || stats is not { } s) return;
            double used = (double)s.Used / s.Total;
            SideDisk.Text = DriveCard.Fmt(s.Used);
            SideDiskLoad.Text = $"{used * 100:0} %";
            Ui.Glide(SideDiskBar, used, LiveMotion);
            SideDiskRow.ToolTip = $"Disco {s.Letter}: {DriveCard.Fmt(s.Used)} em uso de {DriveCard.Fmt(s.Total)}, {DriveCard.Fmt(s.Free)} livres";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { _sideDiskReading = false; }
    }

    private static (string Letter, long Used, long Free, long Total)? ReadSystemDriveUsage()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            if (!drive.IsReady || drive.TotalSize <= 0) return null;
            return (drive.Name.TrimEnd('\\'), drive.TotalSize - drive.TotalFreeSpace, drive.TotalFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private void UpdateCpuSensorWarning()
    {
        if (!_monitor.IsOpen) return; // ainda detectando: não acusar falta de sensor antes da hora
        var problem = _monitor.DiagnoseCpuSensors();
        CpuSensorWarn.Visibility = problem == SensorMonitor.CpuSensorProblem.None ? Visibility.Collapsed : Visibility.Visible;
        CpuSensorWarnText.Text = CpuSensorMessages.Sidebar(problem);
    }

    private async void BtnFixCpuSensors_Click(object sender, RoutedEventArgs e)
    {
        var problem = _monitor.DiagnoseCpuSensors();
        string why = SensorMonitor.Explain(problem);
        if (problem != SensorMonitor.CpuSensorProblem.PawnIOMissing)
        {
            MessageBox.Show(this, why, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!AppInstaller.WingetAvailable)
        {
            MessageBox.Show(this, why + "\n\nO instalador de pacotes do Windows (winget) não foi encontrado neste PC. " +
                "Atualize o \"Instalador de Aplicativo\" pela Microsoft Store e use o botão de novo, " +
                "ou instale o PawnIO (autor: namazso) manualmente.",
                "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this, why + "\n\nInstalar o PawnIO agora? Vai abrir uma janela com o instalador do Windows (winget). " +
            "Quando terminar, feche o CoreBi e abra de novo.",
            "CoreBi", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        using var workload = _workloads.TryBegin("Install PawnIO");
        if (workload == null) { MessageBox.Show(this, "Termine a outra operação antes.", "CoreBi"); return; }
        using var critical = _context.BeginCriticalOperation("Aguarde a instalação do PawnIO terminar antes de fechar o CoreBi.");
        try
        {
            int exitCode = await AppInstaller.InstallInteractiveAsync(AppInstaller.PawnIOPackageId, _context.Lifetime);
            SensorMonitor.RefreshPawnIOStatus();
            MessageBox.Show(this, exitCode == 0
                ? "Instalador concluído. Feche e abra o CoreBi para detectar os sensores novamente."
                : $"O winget encerrou com código 0x{exitCode:X8}. Verifique o instalador antes de tentar novamente.", "CoreBi");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível instalar o PawnIO: " + ex.Message, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal void Theme_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!ThemeManager.IsLight);
        _settings.LightTheme = ThemeManager.IsLight;
        SaveSettings();
    }

    internal void ApplyTheme(bool light)
    {
        ThemeManager.Apply(light);
        UpdateThemeButton();
        RedrawThemedVisuals();
    }

    private void UpdateThemeButton() => ThemeSwitch.IsChecked = !ThemeManager.IsLight;

    private void RedrawThemedVisuals()
    {
        PageDash.RedrawTheme();
        PageStress.DrawChart();
        UpdateCpuSensorWarning();
    }


    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeChrome()
    {
        bool max = WindowState == WindowState.Maximized;
        Root.BorderThickness = max ? new Thickness(0) : new Thickness(1);
        BtnMax.Content = max ? "\uE923" : "\uE922";
        BtnMax.ToolTip = max ? "Restaurar" : "Maximizar";
    }

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
            case "PageSensors": PageSensors.ShowComponent(parts[1]); break;
            case "PageSystem": PageSystem.Select(parts[1]); break;
            case "PageBench": PageBench.SelectTest(parts[1]); break;
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
            case "PageDash": PageDash.UpdateDashboard(); PageDash.RevealRings(); break;
            case "PageOverlay": PageOverlay.UpdatePreview(); break;
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
        SideMonitor.Visibility = name == "PageDash" ? Visibility.Hidden : Visibility.Visible; // Hidden (não Collapsed): o rodapé não pula
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

    private void SyncFrameMonitoring()
    {
        if (_context.IsClosing) return;
        bool needed = PageBottleneck.IsVisible || PageOverlay.IsOverlayLive || PageBottleneck.HasActiveSession;
        if (needed) _frames.Start();
        else if (_frames.Running) _frames.Stop();
    }

    // Sensores e overlay também consultam o driver (GPU e disco): cada consulta gera engasgo no frametime do benchmark.
    private void SetBenchmarkIsolation(bool pauseSensors, bool suspendOverlay)
    {
        _gpuBenchActive = pauseSensors;
        PageOverlay.SuspendForBenchmark(suspendOverlay);
        SyncFrameMonitoring(); // a captura de quadros do overlay também para durante o teste
    }

    /// <summary>Aviso no canto da tela que também fica registrado na atividade recente da Visão geral.</summary>
    private void ShowToast(string title, string detail, string icon)
    {
        PageDash.AddEvent(icon, title, detail.Split('\n')[0], "");
        _toast ??= new ToastWindow();
        _toast.Notify(title, detail, icon);
    }

    private bool SaveSettings()
    {
        try { _settings.Save(); return true; }
        catch (Exception ex)
        {
            DiagnosticLog.Write("settings.save", ex);
            ShowToast("Não foi possível salvar", "A configuração atual não foi gravada. Confira o espaço e as permissões da pasta de dados.", "\uE783");
            return false;
        }
    }
}
