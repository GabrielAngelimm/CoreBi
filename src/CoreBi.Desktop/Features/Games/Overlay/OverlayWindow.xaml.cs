using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CoreBi.Engine.Games;

namespace CoreBi.Desktop;

/// <summary>
/// OSD do desktop: janela transparente, sempre no topo, que ignora cliques e nunca rouba o foco.
/// Aparece sobre jogos em tela cheia sem bordas e na maioria dos DX11/12 (flip model); o caminho RTSS atende tela cheia exclusiva.
/// </summary>
public partial class OverlayWindow : Window
{
    public string Corner { get; set; } = "TL";

    private IntPtr _hwnd, _hook, _monitor;
    private readonly Win32Window.WinEventProc _onForeground; // guardado num campo para o GC não coletar o callback

    public OverlayWindow()
    {
        InitializeComponent();
        _onForeground = (_, _, hwnd, _, _, _, _) => { TrackGame(hwnd); KeepOnTop(); };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32Window.AddExtendedStyles(_hwnd,
                Win32Window.WsExTransparent | Win32Window.WsExLayered | Win32Window.WsExToolWindow | Win32Window.WsExNoActivate);
        };
        SizeChanged += (_, _) => Reposition();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _hook == IntPtr.Zero)
            {
                _hook = Win32Window.SetWinEventHook(Win32Window.EventSystemForeground, Win32Window.EventSystemForeground,
                    IntPtr.Zero, _onForeground, 0, 0, Win32Window.WinEventOutOfContext);
                TrackGame(Win32Window.GetForegroundWindow());
                KeepOnTop();
            }
            else if (!IsVisible) Unhook();
        };
        Closed += (_, _) => Unhook();
    }

    public void SetScale(double s) { Scale.ScaleX = Scale.ScaleY = s; }

    public void Reposition() => KeepOnTop();

    /// <summary>
    /// Cola no canto escolhido do monitor do jogo e mantém acima de janelas sem bordas que também usam HWND_TOPMOST.
    /// Usa a área total do monitor, para ficar sobre jogos em tela cheia. Jogos em janela sem borda também se marcam
    /// "sempre no topo" e passam na frente ao serem ativados; por isso roda a cada troca de janela ativa e a cada atualização.
    /// </summary>
    public void KeepOnTop()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        if (_monitor == IntPtr.Zero) _monitor = Win32Window.MonitorFromPoint(default, Win32Window.MonitorDefaultToPrimary);
        if (!Win32Window.TryGetMonitorInfo(_monitor, out var mi) || !Win32Window.GetWindowRect(_hwnd, out var wr)) return;

        // Tudo em pixels do mesmo sistema de coordenadas (GetMonitorInfo e GetWindowRect), sem conversão de DPI.
        int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
        int m = (int)Math.Round(12 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var r = mi.Monitor;
        int x = Corner.EndsWith('R') ? r.Right - w - m : r.Left + m;
        int y = Corner.StartsWith('B') ? r.Bottom - h - m : r.Top + m;

        var foreground = Win32Window.GetForegroundWindow();
        bool foregroundAbove = Win32Window.IsWindowAbove(_hwnd, foreground);
        // Uma atualização de texto não precisa reenviar posição/z-order ao compositor.
        if (wr.Left == x && wr.Top == y && Win32Window.HasExtendedStyle(_hwnd, Win32Window.WsExTopMost) && !foregroundAbove) return;
        Win32Window.SetWindowPos(_hwnd, Win32Window.HwndTopMost, x, y, 0, 0,
            Win32Window.SwpNoSize | Win32Window.SwpNoActivate | Win32Window.SwpNoOwnerZOrder);
    }

    private void TrackGame(IntPtr fg)
    {
        if (fg == IntPtr.Zero) return;
        Win32Window.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return;
        _monitor = Win32Window.MonitorFromWindow(fg, Win32Window.MonitorDefaultToNearest);
    }

    private void Unhook()
    {
        if (_hook == IntPtr.Zero) return;
        Win32Window.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    public void Update(FrameStats? f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra)
        => Panel.Update(f, lines, show, extra);
}
