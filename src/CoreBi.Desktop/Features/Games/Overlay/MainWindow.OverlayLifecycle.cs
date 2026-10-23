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
            _overlay?.Hide();
        }
    }

    internal void OvOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_overlayReady) return;
        _settings.OverlayCorner = CheckedTag(OvCornerPanel) ?? "TL";
        _settings.OverlayScale = double.Parse(CheckedTag(OvScalePanel) ?? "1", CultureInfo.InvariantCulture);
        SaveSettings();
        ApplyOverlayOptions();
    }

    internal void ApplyOverlayOptions()
    {
        if (_overlay == null) return;
        _overlay.Corner = _settings.OverlayCorner;
        _overlay.SetScale(_settings.OverlayScale);
        _overlay.Reposition();
    }

    internal void UpdateOverlay()
    {
        var f = _frames.GetForegroundStats();
        if (_overlay != null && _overlay.IsVisible)
        {
            _overlay.Update(f, BuildOverlayLines(f), OvShow, OvShow.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null);
            _overlay.KeepOnTop(); // o jogo pode ter passado na frente (janela sem borda "sempre no topo")
        }
        UpdateOverlayStatus(f);
    }

    internal void UpdateOverlayStatus(FrameStats? f)
    {
        if (_frames.Error != null) OvStatus.Text = $"Detecção de FPS indisponível: {_frames.Error}";
        else if (f != null) OvStatus.Text = $"Ativa · {f.ProcessName}: {f.Fps:0} FPS ({f.FrameTimeMs:0.0} ms) · 1% low {f.Low1Fps:0}";
        else OvStatus.Text = "Ativa · aguardando um jogo em primeiro plano";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

}
