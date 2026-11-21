using System.Runtime.InteropServices;

namespace CoreBi.Desktop;

/// <summary>Interoperabilidade Win32/DWM compartilhada por MainWindow, OverlayWindow e ToastWindow.</summary>
internal static class Win32Window
{
    public const int GwlExStyle = -20;
    public const int WsExTopMost = 0x00000008;
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExLayered = 0x00080000;
    public const int WsExNoActivate = 0x08000000;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoOwnerZOrder = 0x0020;
    public const uint EventSystemForeground = 0x0003;
    public const uint WinEventOutOfContext = 0;
    public const uint MonitorDefaultToPrimary = 1;
    public const uint MonitorDefaultToNearest = 2;
    public const uint GwHwndPrev = 3;

    public const int WmGetMinMaxInfo = 0x0024;
    public const int WmDeviceChange = 0x0219;
    public const int WmHotKey = 0x0312;

    public static readonly IntPtr HwndTopMost = new(-1);

    [StructLayout(LayoutKind.Sequential)] public record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] public struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    public static void AddExtendedStyles(IntPtr hwnd, int styles) =>
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | styles);

    public static bool HasExtendedStyle(IntPtr hwnd, int style) =>
        (GetWindowLong(hwnd, GwlExStyle) & style) != 0;

    /// <summary>Cantos arredondados do Windows 11 para janelas sem moldura nativa (ignorado no Windows 10).</summary>
    public static void TryRoundCorners(IntPtr hwnd)
    {
        try
        {
            int round = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Limita janelas sem moldura maximizadas à área útil do monitor para não cobrir a barra de tarefas.</summary>
    public static IntPtr HandleMaximizeMinMax(IntPtr hwnd, int msg, IntPtr lParam)
    {
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero || !TryGetMonitorInfo(monitor, out var info)) return IntPtr.Zero;
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        mmi.MaxPosition = new NativePoint(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
        mmi.MaxSize = new NativePoint(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        Marshal.StructureToPtr(mmi, lParam, false);
        return IntPtr.Zero;
    }

    public static bool TryGetMonitorInfo(IntPtr monitor, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info);
    }

    public static bool IsWindowAbove(IntPtr target, IntPtr candidate)
    {
        var above = GetWindow(target, GwHwndPrev);
        for (int i = 0; above != IntPtr.Zero && i < 128; i++, above = GetWindow(above, GwHwndPrev))
            if (above == candidate) return true;
        return false;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out NativeRect r);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(NativePoint p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr mon, ref MonitorInfo mi);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
