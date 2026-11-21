using System.Diagnostics;

namespace CoreBi.Engine.Performance.Benchmarks;

public sealed record LoadResult(double SeqReadMBs, double SmallFilesMBs, double FilesPerSec, double MapLoadSeconds, string Drive);

/// <summary>
/// Benchmark de carregamento de assets (um pacote grande + arquivos pequenos) lidos fora do cache do Windows.
/// </summary>
public static class LoadingBench
{
    private const long PackBytes = 512L << 20;
    private const int AssetCount = 1000;
    private const int AssetBytes = 64 << 10;

    public static LoadResult Measure(Action<string, double> progress, CancellationToken ct)
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
