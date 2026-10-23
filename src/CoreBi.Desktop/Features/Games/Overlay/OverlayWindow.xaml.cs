using CoreBi.Engine.Games;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// OSD sobre o jogo (estilo RTSS/Afterburner): janela transparente, sempre no topo, que ignora cliques
/// e nunca rouba o foco. Aparece sobre jogos em tela cheia "sem bordas" e na maioria dos DX11/12 em
/// tela cheia (que o Windows apresenta via flip); não aparece em tela cheia exclusiva legada.
/// </summary>
public partial class OverlayWindow : Window
{
    public string Corner { get; set; } = "TL"; // TL, TR, BL, BR

    private IntPtr _hwnd, _hook;
    private IntPtr _monitor;                  // monitor do jogo (o da última janela ativa de outro programa)
    private readonly WinEventProc _onForeground; // guardado num campo para o GC não coletar o callback

    public OverlayWindow()
    {
        InitializeComponent();
        _onForeground = (_, _, hwnd, _, _, _, _) => { TrackGame(hwnd); KeepOnTop(); };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) => Reposition();
        // Só escuta a troca de janela ativa enquanto o overlay está na tela.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _hook == IntPtr.Zero)
            {
                _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground, 0, 0, WINEVENT_OUTOFCONTEXT);
                TrackGame(GetForegroundWindow());
                KeepOnTop();
            }
            else if (!IsVisible) Unhook();
        };
        Closed += (_, _) => Unhook();
    }

    public void SetScale(double s) { Scale.ScaleX = Scale.ScaleY = s; }

    public void Reposition() => KeepOnTop();

    /// <summary>
    /// Cola no canto escolhido do monitor do jogo (área total, para ficar sobre jogos em tela cheia) e volta para o topo.
    /// Jogos em janela sem borda (GTA V, Cyberpunk 2077...) também se marcam como "sempre no topo" e, ao serem ativados,
    /// passam na frente; por isso isto roda a cada troca de janela ativa e a cada atualização do overlay.
    /// </summary>
    public void KeepOnTop()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        if (_monitor == IntPtr.Zero) _monitor = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(_monitor, ref mi) || !GetWindowRect(_hwnd, out var wr)) return;

        // Tudo em pixels do mesmo sistema de coordenadas (GetMonitorInfo e GetWindowRect), sem conversão de DPI.
        int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
        int m = (int)Math.Round(12 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var r = mi.rcMonitor;
        int x = Corner.EndsWith("R") ? r.Right - w - m : r.Left + m;
        int y = Corner.StartsWith("B") ? r.Bottom - h - m : r.Top + m;
        // Uma atualização de texto não precisa reenviar posição/z-order ao compositor.
        var foreground = GetForegroundWindow();
        bool foregroundAbove = false;
        var above = GetWindow(_hwnd, 3); // GW_HWNDPREV
        for (int i = 0; above != IntPtr.Zero && i < 128; i++, above = GetWindow(above, 3))
            if (above == foreground) { foregroundAbove = true; break; }
        if (wr.Left == x && wr.Top == y && (GetWindowLong(_hwnd, GWL_EXSTYLE) & 0x8) != 0 && !foregroundAbove) return;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>Segue o monitor da janela ativa, a não ser que seja do próprio CoreBi.</summary>
    private void TrackGame(IntPtr fg)
    {
        if (fg == IntPtr.Zero) return;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return;
        _monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
    }

    private void Unhook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>Atualiza o overlay. <paramref name="show"/> = itens escolhidos (chaves de OverlayMetrics).</summary>
    public void Update(FrameStats? f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra)
        => Panel.Update(f, lines, show, extra);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
