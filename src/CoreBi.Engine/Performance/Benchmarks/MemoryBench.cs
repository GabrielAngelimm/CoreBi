using System.Diagnostics;

namespace CoreBi.Engine.Performance.Benchmarks;

public sealed record LoadResult(double SeqReadMBs, double SmallFilesMBs, double FilesPerSec, double MapLoadSeconds, string Drive);

/// <summary>
/// Benchmarks de memória (latência por pointer-chasing e banda multi-thread) e de carregamento de assets
/// (um pacote grande + arquivos pequenos) lidos fora do cache do Windows.
/// </summary>
public static class MemoryBench
{
    public const long PackBytes = 512L << 20;
    public const int AssetCount = 1000;
    public const int AssetBytes = 64 << 10;

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

    public static LoadResult MeasureLoading(Action<string, double> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string dir = Path.Combine(Path.GetTempPath(), "CoreBiBench-" + Guid.NewGuid().ToString("N"));
        string drive = Path.GetPathRoot(dir) ?? "";
        try
        {
            Directory.CreateDirectory(dir);
            string pack = Path.Combine(dir, "mapa.pak");

            const int chunk = 4 << 20;
            var block = new byte[chunk];
            new Random(7).NextBytes(block); // dados incompressíveis
            double totalWrite = PackBytes + (long)AssetCount * AssetBytes;

            using (var fs = new FileStream(pack, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough))
            {
                for (long w = 0; w < PackBytes && !ct.IsCancellationRequested; w += block.Length)
                {
                    fs.Write(block);
                    progress("Preparando arquivos de teste", w / totalWrite);
                }
            }
            var assetData = block.AsSpan(0, AssetBytes).ToArray();
            for (int i = 0; i < AssetCount && !ct.IsCancellationRequested; i++)
            {
                File.WriteAllBytes(Path.Combine(dir, $"asset{i:D4}.dat"), assetData);
                if (i % 50 == 0) progress("Preparando arquivos de teste", (PackBytes + (double)i * AssetBytes) / totalWrite);
            }
            ct.ThrowIfCancellationRequested();

            using var aligned = new AlignedBuffer(chunk);
            var buf = aligned.AsSpan();
            var sw = Stopwatch.StartNew();
            using (var handle = File.OpenHandle(pack, FileMode.Open, FileAccess.Read, FileShare.Read, DirectIo.NoBuffering))
            {
                for (long off = 0; off < PackBytes && !ct.IsCancellationRequested; off += chunk)
                {
                    DirectIo.ReadExact(handle, buf, off, "Leitura do pacote de teste incompleta.");
                    progress("Lendo pacote do mapa", off / (double)PackBytes);
                }
            }
            double seqMBs = PackBytes / 1048576.0 / sw.Elapsed.TotalSeconds;

            // Passada não medida antes: o antivírus escaneia cada arquivo recém-criado na 1ª abertura (~30x mais lento),
            // o que não acontece com arquivos de um jogo já instalado.
            progress("Antivírus verificando os arquivos novos", 0);
            ReadAssets(dir, 8, ct);
            sw.Restart();
            progress("Lendo assets pequenos (8 threads)", 0.5);
            ReadAssets(dir, 8, ct);
            double secs = sw.Elapsed.TotalSeconds;
            double mapSeconds = PackBytes / 1048576.0 / seqMBs + secs;
            double smallMBs = (double)AssetCount * AssetBytes / 1048576.0 / secs;
            double filesPerSec = AssetCount / secs;
            ct.ThrowIfCancellationRequested();
            return new LoadResult(seqMBs, smallMBs, filesPerSec, mapSeconds, drive);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static void ReadAssets(string dir, int threads, CancellationToken ct)
    {
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            using var aligned = new AlignedBuffer(AssetBytes);
            var buf = aligned.AsSpan();
            for (int i = t; i < AssetCount && !ct.IsCancellationRequested; i += threads)
            {
                using var handle = File.OpenHandle(Path.Combine(dir, $"asset{i:D4}.dat"), FileMode.Open, FileAccess.Read,
                    FileShare.Read, DirectIo.NoBuffering);
                DirectIo.ReadExact(handle, buf, 0, "Leitura de asset de teste incompleta.");
            }
        });
    }
}
