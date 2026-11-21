using System.Runtime.InteropServices;

namespace CoreBi.Engine.Monitoring.Memory;

/// <summary>Read-only memory status. Does not empty working sets or caches.</summary>
public static class MemoryInfo
{
    public readonly record struct PhysicalUsage(long Total, long Available)
    {
        public long Used => Total - Available;
        public float UsedPercent => Total > 0 ? (float)(100.0 * Used / Total) : 0f;
    }

    /// <summary>RAM visível ao Windows e memória virtual (limite de commit = RAM + arquivo de paginação), numa só amostra.</summary>
    public readonly record struct Snapshot(long Total, long Available, long CommitLimit, long CommitAvailable)
    {
        public long Used => Total - Available;
        public float UsedPercent => Total > 0 ? (float)(100.0 * Used / Total) : 0f;
    }

    /// <summary>GB e percentual devem vir desta mesma amostra de RAM física do Windows.</summary>
    public static PhysicalUsage? ReadPhysicalUsage() =>
        ReadSnapshot() is { } s ? PhysicalUsageFrom(s.Total, s.Available) : null;

    internal static PhysicalUsage? PhysicalUsageFrom(long total, long available) =>
        total > 0 && available >= 0 && available <= total ? new(total, available) : null;

    public static Snapshot? ReadSnapshot()
    {
        var status = new NativeMemoryStatus { Length = (uint)Marshal.SizeOf<NativeMemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0
            ? new((long)status.TotalPhysical, (long)status.AvailablePhysical, (long)status.TotalPageFile, (long)status.AvailablePageFile)
            : null;
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
