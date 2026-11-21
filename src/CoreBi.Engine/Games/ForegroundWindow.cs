using System.Runtime.InteropServices;

namespace CoreBi.Engine.Games;

internal static class ForegroundWindow
{
    public static bool TryGetForeignForeground(out IntPtr hwnd, out int processId)
    {
        hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            processId = 0;
            return false;
        }
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId)
        {
            processId = 0;
            return false;
        }
        processId = (int)pid;
        return true;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
