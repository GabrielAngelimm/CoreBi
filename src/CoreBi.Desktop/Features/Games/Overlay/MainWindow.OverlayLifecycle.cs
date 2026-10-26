using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Engine.Games;

using System.Globalization;

using System.Windows;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    internal OverlayWindow? _overlay;
    internal DispatcherTimer? _overlayTimer;
    internal bool _overlayReady;
    private readonly RtssOverlayClient _rtssOverlay = new();
    private RtssStatus _rtssStatus = RtssStatus.Unavailable;
    private bool _rtssShowing;
    private int _rtssProcess;
    private long _rtssLastUpdate, _rtssLastAck, _rtssRetryAfter;
    internal const int HotkeyId = 0x5245; // "RE"

    // ---------------- Overlay ----------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(HotkeyHook);
        // Ctrl + Shift + O, sem repetição ao segurar
        RegisterHotKey(hwnd, HotkeyId, 0x0002 | 0x0004 | 0x4000, 0x4F);
    }

    internal IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId) // WM_HOTKEY
        {
            ChkOverlay.IsChecked = ChkOverlay.IsChecked != true;
            handled = true;
        }
        else if (msg == 0x0219) // WM_DEVICECHANGE
            OnDeviceChange(wParam.ToInt32(), lParam);
        return IntPtr.Zero;
    }

    internal void ChkOverlay_Changed(object sender, RoutedEventArgs e)
    {
        UpdateFrameMonitoring();
        if (ChkOverlay.IsChecked == true)
        {
            _rtssRetryAfter = 0;
            _overlay ??= new OverlayWindow();
            ApplyOverlayOptions();
            _overlay.Show();
            _overlayTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal,
                                                  (_, _) => UpdateOverlay(), Dispatcher);
            _overlayTimer.Start();
            UpdateOverlay();
        }
        else
        {
            _overlayTimer?.Stop();
            _rtssOverlay.Clear();
            _rtssShowing = false;
            _rtssPublishedFrame = 0;
            _rtssLastAck = 0;
            _overlay?.Hide();
            UpdateOverlayStatus(null);
        }
        UpdateOverlayPreview();
    }

    internal void OvOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_overlayReady) return;
        _settings.OverlayCorner = CheckedTag(OvCornerPanel) ?? "TL";
        _settings.OverlayScale = double.Parse(CheckedTag(OvScalePanel) ?? "1", CultureInfo.InvariantCulture);
        SaveSettings();
        ApplyOverlayOptions();
        PageOverlay.LayoutPreview(_settings.OverlayCorner, _settings.OverlayScale);
    }

    internal void OvOpacity_Changed(double opacity)
    {
        if (!_overlayReady) return;
        _settings.OverlayOpacity = opacity;
        // Arrastar dispara dezenas de mudanças por segundo: o overlay acompanha na hora, mas o disco só é gravado quando o movimento para.
        if (_overlay != null) _overlay.Opacity = opacity;
        _opacitySave ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _opacitySave.Tick -= OpacitySave_Tick;
        _opacitySave.Tick += OpacitySave_Tick;
        _opacitySave.Stop();
        _opacitySave.Start();
    }

    private DispatcherTimer? _opacitySave;

    private void OpacitySave_Tick(object? sender, EventArgs e)
    {
        _opacitySave?.Stop();
        SaveSettings();
    }

    internal void ApplyOverlayOptions()
    {
        if (_overlay == null) return;
        _overlay.Corner = _settings.OverlayCorner;
        _overlay.SetScale(_settings.OverlayScale);
        _overlay.Opacity = _settings.OverlayOpacity;
        _overlay.Reposition();
    }

    internal void UpdateOverlay()
    {
        if (ChkOverlay.IsChecked != true) return;
        var f = _frames.GetForegroundStats();
        var lines = BuildOverlayLines(f);
        string? extra = OvShow.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null;
        _overlay?.Update(f, lines, OvShow, extra);
        UpdateFullscreenOverlay(f);
        if (_overlay != null)
        {
            if (_rtssShowing)
            {
                if (_overlay.IsVisible) _overlay.Hide();
            }
            else
            {
                if (!_overlay.IsVisible) _overlay.Show();
                _overlay.KeepOnTop();
            }
        }
        UpdateOverlayStatus(f);
    }

    private void UpdateFullscreenOverlay(FrameStats? frames)
    {
        long now = Environment.TickCount64;
        int pid = OverlayTarget.FullscreenForegroundProcess(out int viewportWidth, out int viewportHeight);
        if (pid == 0 || pid != _rtssProcess)
        {
            _rtssOverlay.Clear();
            _rtssShowing = false;
            _rtssLastUpdate = 0;
            _rtssLastAck = 0;
            _rtssPendingSince = 0;
            _rtssPublishedFrame = 0;
            _rtssProcess = pid;
        }
        if (pid == 0 || now < _rtssRetryAfter) return;
        var status = _rtssOverlay.Inspect(pid, out var target);
        if (status == RtssStatus.Ready && target.Active)
        {
            if (frames?.ProcessId != pid) frames = null;
            if (_overlay == null) return;
            status = _rtssOverlay.Publish(RtssOverlayFormatter.Format(_overlay.Panel,
                _settings.OverlayCorner, _settings.OverlayScale, _settings.OverlayOpacity, viewportWidth, viewportHeight, frames == null));
            if (status == RtssStatus.Ready)
            {
                _rtssLastUpdate = now;
                if (_rtssPendingSince == 0) _rtssPendingSince = now;
                // Só oculta a janela WPF depois que o jogo consumiu uma atualização OSD anterior.
                if (_rtssPublishedFrame != 0 && unchecked((int)(target.RenderedFrame - _rtssPublishedFrame)) >= 0)
                {
                    _rtssLastAck = now;
                    _rtssPublishedFrame = _rtssOverlay.PublishedFrame;
                }
                else if (_rtssLastAck != 0 && now - _rtssLastAck > 2000)
                {
                    // O servidor pode ter reiniciado entre duas consultas sem passar por Unavailable.
                    _rtssLastAck = 0;
                    _rtssPublishedFrame = 0;
                }
                if (_rtssPublishedFrame == 0) _rtssPublishedFrame = _rtssOverlay.PublishedFrame;
                // Esconde a janela WPF já na primeira publicação (carência de 2 s até o jogo consumir o OSD),
                // para não exibir os dois painéis sobrepostos; sem confirmação, a janela WPF volta.
                _rtssShowing = _rtssLastAck != 0 ? now - _rtssLastAck <= 2000 : now - _rtssPendingSince <= 2000;
            }
        }
        else if (status == RtssStatus.Ready)
        {
            _rtssOverlay.Clear();
            _rtssShowing = false;
            _rtssPublishedFrame = 0;
            _rtssLastAck = 0;
            _rtssPendingSince = 0;
        }
        _rtssStatus = status;
        // Uma disputa curta pelo lock não alterna os renderizadores nem bloqueia o dispatcher.
        if (status != RtssStatus.Ready && (status != RtssStatus.Busy || now - _rtssLastUpdate > 2000))
        {
            _rtssOverlay.Clear();
            _rtssShowing = false;
            _rtssPublishedFrame = 0;
            _rtssLastAck = 0;
            _rtssPendingSince = 0;
            if (status is RtssStatus.Unavailable or RtssStatus.Incompatible) _rtssRetryAfter = now + 5000;
        }
    }

    private uint _rtssPublishedFrame;
    private long _rtssPendingSince;

    /// <summary>Estado da detecção de FPS, com ponto de veredito: medindo um jogo, aguardando, desligada ou com erro.</summary>
    internal void UpdateOverlayStatus(FrameStats? f)
    {
        (string text, string brush) = _rtssShowing ? ("Overlay publicado no jogo pelo RTSS", "Ok")
            : _frames.Error != null ? ($"Indisponível: {_frames.Error}", "Danger")
            : !_frames.Running ? ("", "Faint")
            : f != null ? ($"{f.ProcessName}: {f.Fps:0} FPS ({f.FrameTimeMs:0.0} ms) · 1% low {f.Low1Fps:0}", "Ok")
            : ("Aguardando um jogo · o FPS aparece assim que ele abrir", "Accent");
        OvStatus.Text = text;
        OvStatusDot.Visibility = text.Length == 0 ? Visibility.Hidden : Visibility.Visible; // desligado: a linha fica em branco, sem aviso
        // Só problemas do RTSS merecem dica; o estado normal já diz tudo.
        OvStatus.ToolTip = _rtssShowing ? null : _rtssStatus switch
        {
            RtssStatus.Unavailable => "RTSS não encontrado: instale e abra o RTSS 7.3.7+ antes do jogo para ver o overlay em tela cheia exclusiva.",
            RtssStatus.Incompatible => "RTSS: protocolo incompatível ou ainda não inicializado. Atualize o RTSS.",
            RtssStatus.SlotsFull => "RTSS: todos os slots de OSD estão ocupados por outros programas.",
            RtssStatus.ContentTooLarge => "RTSS: os itens escolhidos excedem a capacidade do OSD.",
            _ => null
        }; OvStatusDot.SetResourceReference(Shape.FillProperty, brush);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

}
