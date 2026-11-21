using System.Runtime.InteropServices;

namespace CoreBi.Engine.Performance.Graphics;

/// <summary>
/// Janela Win32 dedicada para renderização Direct3D fora da árvore WPF. Bloqueia SC_KEYMENU para que a tecla ALT
/// não abra o loop modal de menu e pause o benchmark; fechar ou ESC sinaliza <see cref="StopRequested"/>.
/// </summary>
internal sealed class RenderWindow : IDisposable
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WmClose = 0x0010;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysCommand = 0x0112;
    private const long ScKeyMenu = 0xF100;
    private const int VkEscape = 0x1B;
    private const int IdcArrow = 32512;

    private readonly string _className;
    private readonly IntPtr _hInstance;
    private readonly WndProc _wndProc; // mantido vivo enquanto a janela existir
    private IntPtr _hwnd;
    private volatile bool _stopRequested;

    public IntPtr Handle => _hwnd;
    public bool StopRequested => _stopRequested;

    private RenderWindow(string title, int width, int height, bool fullscreen, bool fitToScreen)
    {
        _className = "CoreBiRender_" + Guid.NewGuid().ToString("N");
        _hInstance = GetModuleHandleW(null);
        _wndProc = WindowProc;

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)IdcArrow),
            lpszClassName = _className,
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new InvalidOperationException("Não foi possível registrar a classe da janela de renderização.");

        try
        {
            int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
            int x, y, w, h;
            uint style;
            if (fullscreen)
            {
                style = WsPopup | WsVisible;
                x = 0; y = 0; w = sw; h = sh;
            }
            else
            {
                style = WsOverlappedWindow | WsVisible;
                double scale = fitToScreen ? Math.Min(1.0, Math.Min(sw * 0.85 / width, sh * 0.85 / height)) : 1.0;
                var rect = new RECT { Right = (int)(width * scale), Bottom = (int)(height * scale) };
                AdjustWindowRect(ref rect, style, false);
                w = rect.Right - rect.Left;
                h = rect.Bottom - rect.Top;
                x = Math.Max(0, (sw - w) / 2);
                y = Math.Max(0, (sh - h) / 2);
            }

            _hwnd = CreateWindowExW(0, _className, title, style, x, y, w, h, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Não foi possível criar a janela de renderização.");
        }
        catch
        {
            UnregisterClassW(_className, _hInstance);
            throw;
        }
    }

    public static RenderWindow CreateFitted(string title, int renderWidth, int renderHeight)
        => new(title, renderWidth, renderHeight, fullscreen: false, fitToScreen: true);

    public static RenderWindow Create(string title, int width, int height, bool fullscreen)
        => new(title, width, height, fullscreen, fitToScreen: false);

    public void PumpMessages()
    {
        while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    public (int Width, int Height) GetClientSize()
    {
        GetClientRect(_hwnd, out var rect);
        return (rect.Right, rect.Bottom);
    }

    public void SetTitle(string title) => SetWindowTextW(_hwnd, title);

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // ALT sozinho abriria o "modo menu" da janela, um loop modal que pausaria a renderização.
        if (msg == WmSysCommand && ((long)wParam & 0xFFF0) == ScKeyMenu) return IntPtr.Zero;
        if (msg == WmClose || (msg == WmKeyDown && wParam == (IntPtr)VkEscape))
        {
            _stopRequested = true;
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        UnregisterClassW(_className, _hInstance);
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string cls, IntPtr hInst);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h,
                                                 IntPtr parent, IntPtr menu, IntPtr hInst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRect(ref RECT r, uint style, bool menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(IntPtr hWnd, string text);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr hInst, IntPtr name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
}
