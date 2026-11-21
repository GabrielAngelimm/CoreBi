using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Games;

/// <summary>
/// Referências APROXIMADAS (compiladas de benchmarks públicos) de desempenho relativo em jogos, usadas só no
/// equilíbrio teórico do Gargalo. Modelo fora das tabelas fica sem referência (null): nenhum desempenho é presumido.
/// </summary>
public static class HardwareCatalog
{
    private sealed record Entry(Regex Pattern, int Length, double Index);

    private static readonly Regex IntelFSuffix = new(@"(\d{4,5}K?)F\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Desempenho relativo em jogos (raster), RTX 4070 = 100. Aproximado.
    private static readonly Entry[] GpuIndex = Compile(
        ("RTX 5090", 265), ("RTX 5080", 175), ("RTX 5070 Ti", 150), ("RTX 5070", 118), ("RTX 5060 Ti", 95), ("RTX 5060", 85),
        ("RTX 4090", 210), ("RTX 4080 SUPER", 160), ("RTX 4080", 157), ("RTX 4070 Ti SUPER", 136), ("RTX 4070 Ti", 124),
        ("RTX 4070 SUPER", 116), ("RTX 4070", 100), ("RTX 4060 Ti", 82), ("RTX 4060", 70),
        ("RTX 3090 Ti", 135), ("RTX 3090", 125), ("RTX 3080 Ti", 122), ("RTX 3080", 112), ("RTX 3070 Ti", 92), ("RTX 3070", 86),
        ("RTX 3060 Ti", 78), ("RTX 3060", 60), ("RTX 3050", 40), ("GTX 1660", 38), ("GTX 1650", 28),
        ("RX 9070 XT", 160), ("RX 9070", 140), ("RX 7900 XTX", 170), ("RX 7900 XT", 150), ("RX 7900 GRE", 128),
        ("RX 7800 XT", 115), ("RX 7700 XT", 98), ("RX 7600 XT", 72), ("RX 7600", 70),
        ("RX 6950 XT", 125), ("RX 6800 XT", 112), ("RX 6700 XT", 82), ("RX 6600", 58),
        ("Arc B580", 80), ("Arc A770", 68),
        ("Radeon(TM) Graphics", 6), ("Radeon Graphics", 6));

    // Desempenho relativo em jogos (limitado pela CPU), Ryzen 7 7700X = 100. Aproximado.
    private static readonly Entry[] CpuIndex = Compile(
        ("9950X3D", 138), ("9800X3D", 140), ("7950X3D", 126), ("7800X3D", 128), ("5800X3D", 95), ("5700X3D", 90),
        ("9950X", 112), ("9900X", 110), ("9700X", 108), ("9600X", 104), ("7950X", 104), ("7900X", 102), ("7700X", 100),
        ("7700", 97), ("7600X", 96), ("7600", 93), ("5950X", 82), ("5900X", 81), ("5800X", 80), ("5700X", 77), ("5600X", 76), ("5600", 73),
        ("285K", 112), ("265K", 108), ("245K", 100), ("14900K", 116), ("14700K", 112), ("14600K", 104), ("13900K", 113),
        ("13700K", 108), ("13600K", 101), ("13400", 84), ("12900K", 98), ("12700K", 95), ("12600K", 90), ("12400", 78));

    public static double? GpuPerf(string gpuName) =>
        HardwareNaming.IsMobileGpu(gpuName)
            ? null
            : Lookup(GpuIndex, gpuName) ?? Lookup(GpuIndex, HardwareNaming.ShortGpuName(gpuName));

    public static double? CpuPerf(string cpuName) =>
        Lookup(CpuIndex, IntelFSuffix.Replace(cpuName, "$1"));

    /// <summary>A chave mais longa contida no nome vence ("RTX 5070 Ti" antes de "RTX 5070").</summary>
    private static Entry[] Compile(params (string Key, double Index)[] rows) =>
        rows.OrderByDescending(t => t.Key.Length)
            .Select(t => new Entry(
                new Regex(@"(?<![A-Za-z0-9])" + Regex.Escape(t.Key) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                t.Key.Length,
                t.Index))
            .ToArray();

    private static double? Lookup(Entry[] table, string name)
    {
        foreach (var entry in table)
            if (entry.Pattern.IsMatch(name)) return entry.Index;
        return null;
    }
}
