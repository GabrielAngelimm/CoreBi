namespace CoreBi.Engine.Infrastructure;

/// <summary>Estatísticas comuns de frametime e passadas (percentil 99 / 1% low e mediana).</summary>
internal static class FrameTimeStats
{
    public static double Percentile99(IReadOnlyCollection<double> frameTimesMs)
    {
        if (frameTimesMs.Count == 0) return 0;
        var sorted = frameTimesMs.OrderBy(x => x).ToArray();
        int idx = Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99));
        return sorted[idx];
    }

    public static double Low1Fps(IReadOnlyCollection<double> frameTimesMs)
    {
        double p99 = Percentile99(frameTimesMs);
        return p99 > 0 ? 1000.0 / p99 : 0;
    }

    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return 0;
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;
    }
}
