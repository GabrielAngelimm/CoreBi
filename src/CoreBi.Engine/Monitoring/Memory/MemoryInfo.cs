using System.Runtime.InteropServices;
namespace CoreBi.Engine.Monitoring.Memory;

/// <summary>Read-only memory status. Does not empty working sets or caches.</summary>
public static class MemoryInfo
{
    public static (long Total, long Available) MemoryStatus()
    {
        var status = new NativeMemoryStatus { Length = (uint)Marshal.SizeOf<NativeMemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? ((long)status.TotalPhysical, (long)status.AvailablePhysical) : (0, 0);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref NativeMemoryStatus status);
}

