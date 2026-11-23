using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Win32.SafeHandles;

namespace CoreBi.Engine.Performance.Benchmarks;

public sealed record DiskBenchResult(string Drive, long FileMb, double SeqReadMBs, double SeqWriteMBs,
                                     double ReadIopsQ1, double WriteIopsQ1, double ReadLatencyUs, double WriteLatencyUs,
                                     double ReadIopsQ8)
{
    /// <summary>Nota só para a barra do histórico: sequencial pesa em cópia/carregamento, 4K em abrir programas e sistema.</summary>
    public double Score => (SeqReadMBs + SeqWriteMBs) / 2 + (ReadIopsQ1 + WriteIopsQ1) / 200.0 + ReadIopsQ8 / 2000.0;
}

/// <summary>
/// Benchmark de armazenamento: leitura e escrita sequenciais (blocos de 1 MiB, 8 pedidos na fila) e acesso aleatório de 4 KiB
/// (1 e 8 em paralelo). Usa FILE_FLAG_NO_BUFFERING para medir o disco fora do cache do Windows e dá MB/s decimais
/// (1 MB = 1.000.000 bytes), a unidade dos fabricantes e dos outros benchmarks de disco.
/// </summary>
/// <remarks>
/// Por que 8 na fila: um SSD NVMe só chega à velocidade da caixa com vários pedidos em andamento. Com um pedido por vez
/// (como era nas versões de teste), cada bloco espera o anterior voltar e um NVMe de 7.000 MB/s para perto de 3.000.
/// </remarks>
public static class DiskBench
{
    private const int Block = 1 << 20;
    private const int Small = 4096;
    private const int SeqDepth = 8;
    private const double SeqSeconds = 3;
    private const double RandomSeconds = 4;

    /// <summary>Unidades fixas e removíveis prontas com pelo menos 1,5 GB livres.</summary>
    public static List<(string Root, string Text)> Drives()
    {
        var list = new List<(string, string)>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                if (d.TotalFreeSpace < 1536L << 20) continue;
                string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $" {d.VolumeLabel}";
                list.Add((d.Name, $"{d.Name.TrimEnd('\\')}{label}"));
            }
            catch { }
        }
        return list;
    }

    public static DiskBenchResult Run(string driveRoot, long fileMb, Action<string, double> progress, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileMb);
        ct.ThrowIfCancellationRequested();
        long fileBytes = checked(fileMb * Block);
        var di = new DriveInfo(driveRoot);
        if (di.TotalFreeSpace < checked(fileBytes + (512L << 20)))
            throw new IOException($"Pouco espaço livre em {driveRoot} (precisa de {fileMb + 512} MB).");

        // O arquivo de teste é criado numa pasta própria da unidade e apagado no final.
        string dir = Path.Combine(driveRoot, "CoreBiBench-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "bench.tmp");
        Directory.CreateDirectory(dir);

        try
        {
            var buffers = new AlignedBuffer[SeqDepth];
            try
            {
                for (int i = 0; i < SeqDepth; i++)
                {
                    buffers[i] = new AlignedBuffer(Block);
                    new Random(42 + i).NextBytes(buffers[i].AsSpan()); // dados incompressíveis (impedem compressão pelo controlador)
                }

                // Preparo: o arquivo é escrito inteiro antes de medir. No NTFS, uma escrita que aumenta o arquivo é sempre
                // síncrona, então a fila de 8 só funciona sobre um arquivo que já existe no tamanho final.
                using (var create = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
                           DirectIo.NoBuffering, fileBytes))
                {
                    var fill = buffers[0].AsSpan();
                    for (long off = 0; off < fileBytes; off += Block)
                    {
                        ct.ThrowIfCancellationRequested();
                        RandomAccess.Write(create, fill, off);
                        progress("Preparando o arquivo de teste", 0.10 * off / fileBytes);
                    }
                    RandomAccess.FlushToDisk(create);
                }

                double seqWrite, seqRead;
                using (var queued = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
                           FileOptions.Asynchronous | DirectIo.NoBuffering))
                {
                    var memory = buffers.Select(b => b.AsMemory()).ToArray();
                    seqWrite = Sequential(queued, memory, fileBytes, write: true, "Escrita sequencial (1 MB, 8 na fila)", 0.10, 0.20, progress, ct);
                    RandomAccess.FlushToDisk(queued); // fora do tempo, como nos outros benchmarks: só garante o arquivo antes de ler
                    seqRead = Sequential(queued, memory, fileBytes, write: false, "Leitura sequencial (1 MB, 8 na fila)", 0.30, 0.20, progress, ct);
                }

                long slots = fileBytes / Small;
                double readIops, writeIops, readLat, writeLat;
                using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, DirectIo.NoBuffering))
                {
                    var small = buffers[0].AsSpan(Small);
                    readIops = RandomLoop(handle, small, slots, write: false, progress, "Leitura 4 KB aleatória (1 por vez)", 0.50, 0.15, ct, out readLat);
                    writeIops = RandomLoop(handle, small, slots, write: true, progress, "Escrita 4 KB aleatória (1 por vez)", 0.65, 0.15, ct, out writeLat);
                }

                progress("Leitura 4 KB aleatória (8 em paralelo)", 0.80);
                double q8 = ParallelRead(path, slots, 8, ct, progress);

                progress("Concluído", 1.0);
                return new DiskBenchResult(driveRoot.TrimEnd('\\'), fileMb, seqRead, seqWrite, readIops, writeIops, readLat, writeLat, q8);
            }
            finally
            {
                foreach (var b in buffers) b?.Dispose();
            }
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { Directory.Delete(dir); } catch { }
        }
    }

    /// <summary>
    /// Blocos de 1 MiB em ordem, com <paramref name="buffers"/>.Length pedidos sempre em andamento no mesmo arquivo
    /// (E/S overlapped). Roda por <see cref="SeqSeconds"/> e volta ao começo do arquivo quando chega ao fim. Devolve MB/s decimais.
    /// </summary>
    private static double Sequential(SafeFileHandle handle, Memory<byte>[] buffers, long fileBytes, bool write, string phase,
                                     double start, double span, Action<string, double> progress, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long next = 0, done = 0;
        var sw = Stopwatch.StartNew();
        async Task Lane(Memory<byte> buffer)
        {
            try
            {
                while (sw.Elapsed.TotalSeconds < SeqSeconds && !stop.IsCancellationRequested)
                {
                    long offset = (Interlocked.Add(ref next, Block) - Block) % fileBytes;
                    if (write) await RandomAccess.WriteAsync(handle, buffer, offset, stop.Token).ConfigureAwait(false);
                    else if (await RandomAccess.ReadAsync(handle, buffer, offset, stop.Token).ConfigureAwait(false) != buffer.Length)
                        throw new EndOfStreamException("Leitura sequencial incompleta.");
                    Interlocked.Add(ref done, buffer.Length);
                }
            }
            catch when (!stop.IsCancellationRequested)
            {
                stop.Cancel(); // uma falha para as outras filas; o erro original sobe pelo WhenAll
                throw;
            }
        }

        var all = Task.WhenAll(buffers.Select(b => Lane(b)));
        // WaitAny não lança: o resultado das filas é lido só depois que todas terminaram (o arquivo não fecha com pedido no ar)
        while (Task.WaitAny([all], 150) < 0)
            progress(phase, start + span * Math.Min(1, sw.Elapsed.TotalSeconds / SeqSeconds));
        double seconds = sw.Elapsed.TotalSeconds;
        ct.ThrowIfCancellationRequested();
        if (all.Exception is { } failure)
        {
            // A falha de verdade, não o cancelamento que ela causou nas outras filas
            var cause = failure.InnerExceptions.FirstOrDefault(e => e is not OperationCanceledException) ?? failure.InnerExceptions[0];
            ExceptionDispatchInfo.Throw(cause);
        }
        return Interlocked.Read(ref done) / 1e6 / seconds;
    }

    private static double RandomLoop(SafeFileHandle handle, Span<byte> buf, long slots, bool write,
                                     Action<string, double> progress, string phase, double start, double span,
                                     CancellationToken ct, out double latencyUs)
    {
        var rng = new Random(write ? 7 : 3);
        long ops = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < RandomSeconds)
        {
            // Checa o cancelamento a cada 64 operações (barato) e atualiza a barra.
            for (int i = 0; i < 64; i++)
            {
                long off = rng.NextInt64(slots) * Small;
                if (write) RandomAccess.Write(handle, buf, off);
                else DirectIo.ReadExact(handle, buf, off, "Leitura aleatória incompleta.");
                ops++;
            }
            ct.ThrowIfCancellationRequested();
            progress(phase, start + span * Math.Min(1, sw.Elapsed.TotalSeconds / RandomSeconds));
        }
        double secs = sw.Elapsed.TotalSeconds;
        latencyUs = secs / ops * 1e6;
        return ops / secs;
    }

    private static double ParallelRead(string path, long slots, int threads,
                                       CancellationToken ct, Action<string, double> progress)
    {
        long total = 0;
        var sw = Stopwatch.StartNew();
        var tasks = new Task[threads];
        for (int t = 0; t < threads; t++)
        {
            int seed = 100 + t;
            tasks[t] = Task.Factory.StartNew(() =>
            {
                using var aligned = new AlignedBuffer(Small);
                using var workerHandle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, DirectIo.NoBuffering);
                var buf = aligned.AsSpan();
                var rng = new Random(seed);
                long local = 0;
                while (sw.Elapsed.TotalSeconds < RandomSeconds && !ct.IsCancellationRequested)
                {
                    for (int i = 0; i < 32; i++)
                    {
                        DirectIo.ReadExact(workerHandle, buf, rng.NextInt64(slots) * Small, "Leitura paralela incompleta.");
                        local++;
                    }
                }
                Interlocked.Add(ref total, local);
            }, TaskCreationOptions.LongRunning);
        }
        var completion = Task.WhenAll(tasks);
        try
        {
            while (!completion.Wait(150))
                progress("Leitura 4 KB aleatória (8 em paralelo)", 0.80 + 0.19 * Math.Min(1, sw.Elapsed.TotalSeconds / RandomSeconds));
        }
        finally
        {
            // Never delete the file while another worker still owns a handle.
            try { completion.GetAwaiter().GetResult(); } catch (Exception) { /* Observed by Wait above. */ }
        }
        ct.ThrowIfCancellationRequested();
        return total / sw.Elapsed.TotalSeconds;
    }
}
