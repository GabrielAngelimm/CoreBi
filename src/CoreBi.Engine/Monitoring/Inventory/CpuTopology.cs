using System.Numerics;
using System.Runtime.InteropServices;

namespace CoreBi.Engine.Monitoring.Inventory;

/// <summary>Um núcleo físico: quantas threads ele roda e a classe de eficiência (híbridos: P-core &gt; E-core).</summary>
public sealed record CpuCore(int Threads, int EfficiencyClass);

/// <summary>Um nível de cache: tamanho de cada instância e quantas existem (ex.: L2 = 8 × 512 KB).</summary>
public sealed record CpuCache(int Level, string Kind, long Size, int Instances, int Ways, int LineSize);

/// <summary>
/// Núcleos e caches como o Windows os enxerga (GetLogicalProcessorInformationEx), a mesma fonte do CPU-Z.
/// Diferente do WMI, separa L1 de dados e de instruções, conta núcleos de desempenho e de eficiência e
/// diz quais núcleos têm SMT (duas threads).
/// </summary>
public sealed class CpuTopology
{
    public List<CpuCore> Cores { get; } = new();
    public List<CpuCache> Caches { get; } = new();

    public int Threads => Cores.Sum(c => c.Threads);
    public bool Hybrid => Cores.Select(c => c.EfficiencyClass).Distinct().Count() > 1;
    public int PerformanceCores => Hybrid ? Cores.Count(c => c.EfficiencyClass == Cores.Max(x => x.EfficiencyClass)) : Cores.Count;
    public int EfficientCores => Cores.Count - PerformanceCores;
    public CpuCache? Cache(int level, string kind) => Caches.FirstOrDefault(c => c.Level == level && c.Kind == kind);

    private const int RelationProcessorCore = 0, RelationCache = 2, RelationAll = 0xFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    public static CpuTopology Read()
    {
        var topo = new CpuTopology();
        uint len = 0;
        GetLogicalProcessorInformationEx(RelationAll, IntPtr.Zero, ref len);
        if (len == 0) return topo;
        IntPtr buf = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationAll, buf, ref len)) return topo;
            var caches = new Dictionary<(int, string, long), (int Count, int Ways, int Line)>();
            int off = 0;
            while (off < len)
            {
                IntPtr p = buf + off;
                int rel = Marshal.ReadInt32(p);
                int size = Marshal.ReadInt32(p, 4);
                if (size <= 0) break;
                if (rel == RelationProcessorCore)
                {
                    // PROCESSOR_RELATIONSHIP: Flags(8) EfficiencyClass(9) Reserved[20] GroupCount(30) GroupMask[](32)
                    int eff = Marshal.ReadByte(p, 9);
                    int groups = Marshal.ReadInt16(p, 30);
                    int threads = 0;
                    for (int g = 0; g < groups; g++)
                        threads += BitOperations.PopCount((ulong)Marshal.ReadIntPtr(p, 32 + g * 16).ToInt64());
                    topo.Cores.Add(new CpuCore(Math.Max(1, threads), eff));
                }
                else if (rel == RelationCache)
                {
                    // CACHE_RELATIONSHIP: Level(8) Associativity(9) LineSize(10) CacheSize(12) Type(16)
                    int level = Marshal.ReadByte(p, 8);
                    int ways = Marshal.ReadByte(p, 9);
                    int line = Marshal.ReadInt16(p, 10);
                    long bytes = (uint)Marshal.ReadInt32(p, 12);
                    string kind = Marshal.ReadInt32(p, 16) switch { 1 => "instruções", 2 => "dados", 3 => "trace", _ => "" };
                    var key = (level, kind, bytes);
                    caches[key] = caches.TryGetValue(key, out var c) ? (c.Count + 1, ways, line) : (1, ways, line);
                }
                off += size;
            }
            foreach (var ((level, kind, bytes), c) in caches.OrderBy(k => k.Key.Item1).ThenBy(k => k.Key.Item2 == "dados" ? 0 : 1))
                if (kind != "trace") topo.Caches.Add(new CpuCache(level, kind, bytes, c.Count, c.Ways, c.Line));
        }
        catch { }
        finally { Marshal.FreeHGlobal(buf); }
        return topo;
    }
}
