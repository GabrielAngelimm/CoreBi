using System.Diagnostics;

namespace CoreBi.Engine.Performance.Benchmarks;

/// <summary>Benchmarks de memória (latência por pointer-chasing e banda multi-thread).</summary>
public static class MemoryBench
{
    private const long LatencyBytes = 256L << 20;  // 256 MB: bem maior que qualquer cache L3
    private const long BandwidthBytes = 1L << 30;  // 1 GB

    /// <summary>
    /// Percorre uma permutação de Sattolo (um único ciclo) em saltos de 64 bytes para medir a latência real da RAM.
    /// </summary>
    public static double MeasureLatencyNs(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int n = (int)(LatencyBytes / sizeof(long));
        var next = new long[n];
        int lines = n / 8;
        var order = new int[lines];
        for (int i = 0; i < lines; i++) order[i] = i;
        var rng = new Random(42);
        for (int i = lines - 1; i > 0; i--) { int j = rng.Next(i); (order[i], order[j]) = (order[j], order[i]); }
        for (int i = 0; i < lines; i++) next[order[i] * 8] = order[(i + 1) % lines] * 8;

        long p = 0;
        for (int i = 0; i < 200_000; i++) p = next[p]; // aquece TLB/páginas
        const int steps = 20_000_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++)
        {
            p = next[p];
            if ((i & 0xFFFFF) == 0) ct.ThrowIfCancellationRequested();
        }
        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / steps;
        GC.KeepAlive(p);
        return ns;
    }

    /// <summary>Banda de leitura (soma) e de cópia com todas as threads, em GB/s (melhor de 3).</summary>
    public static (double Read, double Copy) MeasureBandwidth(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int threads = Environment.ProcessorCount;
        int n = (int)(BandwidthBytes / sizeof(long));
        int per = n / threads;
        int coveredLongs = per * threads;
        long coveredBytes = (long)coveredLongs * sizeof(long);

        var src = new long[coveredLongs];
        var dst = new long[coveredLongs];
        Parallel.For(0, threads, t => Array.Fill(src, t + 1, t * per, per));

        double bestRead = 0, bestCopy = 0;
        for (int rep = 0; rep < 3 && !ct.IsCancellationRequested; rep++)
        {
            var sw = Stopwatch.StartNew();
            long total = 0;
            Parallel.For(0, threads, () => 0L, (t, _, acc) =>
            {
                var span = new ReadOnlySpan<long>(src, t * per, per);
                long s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                int i = 0;
                for (; i + 3 < span.Length; i += 4)
                {
                    s0 += span[i]; s1 += span[i + 1]; s2 += span[i + 2]; s3 += span[i + 3];
                }
                for (; i < span.Length; i++) s0 += span[i];
                return acc + s0 + s1 + s2 + s3;
            }, acc => Interlocked.Add(ref total, acc));
            bestRead = Math.Max(bestRead, coveredBytes / sw.Elapsed.TotalSeconds / 1e9);

            sw.Restart();
            Parallel.For(0, threads, t => Array.Copy(src, t * per, dst, t * per, per));
            // Cópia lê e escreve: conta os dois sentidos.
            bestCopy = Math.Max(bestCopy, 2.0 * coveredBytes / sw.Elapsed.TotalSeconds / 1e9);
        }
        ct.ThrowIfCancellationRequested();
        return (bestRead, bestCopy);
    }

    /// <summary>Pontuação do histórico "mem": combina a latência da RAM com o tempo de carregamento do mapa.</summary>
    public static double Score(double latencyNs, double mapLoadSeconds) => 1000 / latencyNs + 10 / mapLoadSeconds;
}
