using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Persistence;

namespace CoreBi.Desktop;

public partial class MainWindow : Window, ILiveMotionHost
{
    /// <summary>Altura de uma linha da atividade recente na Visão geral (usada pelo binding XAML em OverviewView).</summary>
    public const double DashEventHeight = Views.OverviewView.EventRowHeight;

    private const int HotkeyId = 0x5245; // "RE"

    internal readonly WorkloadCoordinator _workloads = new();
    internal readonly AppSettings _settings = AppSettings.Load();
    internal readonly SensorMonitor _monitor = new();
    internal readonly FeatureContext _context;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly FrameMonitor _frames = new();
    private readonly GpuMonitor _gpuMonitor = new();
    private readonly HardwareDeviceWatcher _deviceWatcher;
    private readonly DispatcherTimer _timer;
    private ToastWindow? _toast;
    private bool _sensorsHeldForBenchmark;
    private bool _polling;

    public MainWindow()
    {
        InitializeComponent();
        _context = new FeatureContext
        {
            Owner = this,
            Workloads = _workloads,
            Lifetime = _lifetime.Token,
            Settings = _settings,
            Sensors = _monitor,
            Frames = _frames,
            Navigate = GoToPage,
            RecordEvent = (icon, title, detail, value, channel) => PageDash.AddEvent(icon, title, detail, value, channel),
            Notify = ShowToast,
            SaveSettings = SaveSettings,
            SyncFrameMonitoring = SyncFrameMonitoring,
            SetBenchmarkIsolation = SetBenchmarkIsolation,
            IsLiveMotion = () => LiveMotion,
        };

        _deviceWatcher = new HardwareDeviceWatcher(_context, Dispatcher, OnStorageChangedAsync);

        ConnectViews();
        ThemeManager.Apply(_settings.LightTheme);
        UpdateThemeButton();
        BuildPalette();
        ShowPage(nameof(PageDash));

        SideVersion.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1";

        _deviceWatcher.StartInitialSnapshots();

        Loaded += async (_, _) => await LoadHardwareAsync();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTelemetryTick;
        _timer.Start();

        StateChanged += (_, _) => UpdateMaximizeChrome();
        WindowState = WindowState.Maximized;
        UpdateMaximizeChrome();
        Loaded += (_, _) => ApplyUiScale(VisualTreeHelper.GetDpi(this));

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

    private async void OnTelemetryTick(object? sender, EventArgs e)
    {
        // Durante benchmarks de GPU/disco as leituras de sensores (NVML/driver) ficam pausadas para não gerar engasgos no 1% low.
        if (_sensorsHeldForBenchmark)
        {
            PageStress.Tick();
            return;
        }

        // A leitura dos sensores leva dezenas de ms (chips da placa-mãe, SMART, NVML): roda fora da janela, para a interface não
        // congelar a cada segundo (a navegação e as animações engasgavam no tique). Se a anterior ainda não voltou, este tique pula.
        if (_polling) return;
        _polling = true;
        SensorPoll? poll;
        try { poll = await _monitor.PollAsync(); }
        catch (Exception ex) { poll = null; DiagnosticLog.Write("sensors.poll", ex); }
        finally { _polling = false; }
        if (_context.IsClosing || _sensorsHeldForBenchmark) return;
        if (poll != null) _monitor.Apply(poll);
        _gpuMonitor.UpdateLoads(_context.Gpus);
        PageSensors.Tick();
        PageStress.Tick();
        PageGpu.Tick();
        UpdateCpuSensorWarning();
        PageDash.Tick();
        PageDisks.Tick();
        PageEnergy.Tick();
        SyncFrameMonitoring();
        PageBottleneck.Tick();
        if (PageBench.IsVisible) PageBench.Tick();
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
                _context.SensorsRead = true;
                PageDash.SensorsReady();
                return;
            }
            if (_context.IsClosing) return;
            if (await _monitor.PollAsync() is { } poll && !_context.IsClosing) _monitor.Apply(poll); // primeira leitura também fora da janela
            if (_context.IsClosing) return;
            _context.SensorsRead = true;
            UpdateCpuSensorWarning();
            PageSensors.Populate();
            PageDash.SensorsReady();
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
            _context.HardwareRead = true;
            PageGpu.PopulateGpus(gpus);
            PageSystem.RefreshHardware();
            PageBottleneck.RefreshHardware();
            PageOverlay.RefreshHardware();
            PageBench.RefreshHardware();
            PageDash.UpdateDashboard();
        }

        await Task.WhenAll(LoadInfo(), LoadSensors(), LoadGpuAndCpu());
    }

    private async Task OnStorageChangedAsync()
    {
        await PageSystem.RefreshStorageAsync();
        if (PageDisks.IsVisible) await PageDisks.RefreshAsync();
        if (PageBench.IsVisible) PageBench.RefreshHardware();
        if (PageDash.IsVisible) PageDash.UpdateDashboard(full: false);
    }


    internal void Theme_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!ThemeManager.IsLight, animate: true);
        _settings.LightTheme = ThemeManager.IsLight;
        SaveSettings();
    }

    internal void ApplyTheme(bool light, bool animate = false)
    {
        if (animate) BeginThemeFade();
        ThemeManager.Apply(light);
        UpdateThemeButton();
        RedrawThemedVisuals();
    }

    private static readonly TimeSpan ThemeFadeTime = TimeSpan.FromMilliseconds(420);

    /// <summary>
    /// Troca de tema sem corte seco: antes de trocar as cores, a tela inteira vira uma foto (no fundo do tema atual, porque a área
    /// das páginas é transparente) posta por cima de tudo; o tema novo é aplicado embaixo dela no mesmo quadro e a foto se dissolve
    /// em 420 ms. Cada cor, gráfico e ícone passa de um tema para o outro ao mesmo tempo, sem animar pincel por pincel. Uma troca
    /// no meio de outra fotografa o que está na tela (a mistura) e segue dali. Sem animações no Windows, a troca é direta.
    /// </summary>
    private void BeginThemeFade()
    {
        if (!SystemParameters.ClientAreaAnimation || ShellGrid.ActualWidth <= 0 || ShellGrid.ActualHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double width = ShellGrid.ActualWidth, height = ShellGrid.ActualHeight;
        var snapshot = new DrawingVisual();
        using (var dc = snapshot.RenderOpen())
        {
            dc.DrawRectangle(ThemeManager.Brush("Bg"), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(ShellGrid) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(snapshot);
        bitmap.Freeze();

        ThemeFade.BeginAnimation(OpacityProperty, null);
        ThemeFade.Source = bitmap;
        ThemeFade.Opacity = 1;
        ThemeFade.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(1, 0, ThemeFadeTime) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (_, _) =>
        {
            if (ThemeFade.Source != bitmap) return; // outra troca começou depois desta
            ThemeFade.Visibility = Visibility.Collapsed;
            ThemeFade.Source = null;
        };
        ThemeFade.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateThemeButton() => (ThemeManager.IsLight ? ThemeLight : ThemeDark).IsChecked = true;

    private void RedrawThemedVisuals()
    {
        PageDash.RedrawTheme();
        PageStress.RedrawTheme();
        UpdateCpuSensorWarning();
    }


    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Tamanho fixo em pixels, qualquer que seja a escala do Windows: a interface foi desenhada em 1920 × 1032 a 100 %, e com
    /// 125 % ou 150 % o Windows aumentaria tudo e a mesma tela cheia caberia em 1536 × 826, com as páginas trocando de arranjo e
    /// rolando. A escala é desfeita até 80 % (125 % do Windows volta exatamente ao desenho; em 150 % a interface fica 20 % maior
    /// que o desenho, com um arranjo mais estreito, para o texto não ficar miúdo). A 100 % não muda nada.
    /// </summary>
    private void ApplyUiScale(DpiScale dpi)
    {
        double scale = Math.Clamp(1 / Math.Max(1, dpi.DpiScaleX), 0.8, 1);
        Root.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
        // Texto encolhido junto com a interface: a medida "Display" (presa à grade de pixels no tamanho original) deixaria o
        // espaçamento torto; a "Ideal" mede no tamanho em que o texto aparece
        TextOptions.SetTextFormattingMode(this, scale == 1 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.CaptionHeight = 52 * scale;
            chrome.ResizeBorderThickness = new Thickness(6 * scale);
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyUiScale(newDpi);
    }

    private void UpdateMaximizeChrome()
    {
        bool max = WindowState == WindowState.Maximized;
        Root.BorderThickness = max ? new Thickness(0) : new Thickness(1);
        BtnMax.Content = max ? "\uE923" : "\uE922";
        BtnMax.ToolTip = max ? "Restaurar" : "Maximizar";
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
        _sensorsHeldForBenchmark = pauseSensors;
        if (pauseSensors) _monitor.WaitForPoll(); // uma leitura em andamento termina antes de o teste começar a medir
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
