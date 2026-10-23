using System.Runtime.InteropServices;

namespace CoreBi.Engine.Games;

/// <summary>Identifica uma janela que ocupa o monitor; isso não distingue fullscreen exclusivo de sem bordas.</summary>
internal static class OverlayTarget
{
    internal static int FullscreenForegroundProcess(out int width, out int height)
    {
        width = height = 0;
        var window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId || !GetClientRect(window, out var client)) return 0;
        var origin = new Point();
        if (!ClientToScreen(window, ref origin)) return 0;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) return 0;
        var screen = info.Monitor;
        if (!CoversMonitor(origin.X, origin.Y, client.Right, client.Bottom, screen.Left, screen.Top, screen.Right, screen.Bottom)) return 0;
        width = client.Right; height = client.Bottom;
        return (int)pid;
    }

    internal static bool CoversMonitor(int x, int y, int width, int height, int left, int top, int right, int bottom)
        => Math.Abs((long)x - left) <= 2 && Math.Abs((long)y - top) <= 2
            && Math.Abs((long)x + width - right) <= 2 && Math.Abs((long)y + height - bottom) <= 2;

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
