using System.Diagnostics;
using CoreBi.Engine.Monitoring.Memory;

namespace CoreBi.Engine.Performance.Stress;

/// <summary>Gera carga sintética de CPU (todos os threads) e/ou RAM (escrita e verificação de padrões).</summary>
public sealed class StressTester : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private readonly List<Thread> _threads = new();
    private long _memErrors;
    private long _memPasses;

    public bool Running { get { lock (_sync) return _cts != null; } }
    public long MemoryErrors => Interlocked.Read(ref _memErrors);
    public long MemoryPasses => Interlocked.Read(ref _memPasses);
    public long MemoryAllocatedBytes { get; private set; }
    public string? Error { get; private set; }

    public void Start(bool cpu, bool ram, double ramFraction = 0.6)
    {
        if (!cpu && !ram) throw new ArgumentException("Selecione CPU ou RAM.");
        lock (_sync)
        {
            if (_cts != null) return;
            Error = null;
            var cts = new CancellationTokenSource();
            _cts = cts;
            var token = cts.Token;
            Interlocked.Exchange(ref _memErrors, 0);
            Interlocked.Exchange(ref _memPasses, 0);
            MemoryAllocatedBytes = 0;

            int cpuThreads = Environment.ProcessorCount;
            // Com RAM ativa, reserva um thread para o worker de memória.
            if (cpu && ram && cpuThreads > 1) cpuThreads--;

            if (cpu)
            {
                for (int i = 0; i < cpuThreads; i++)
                {
                    int seed = i;
                    StartThread(cts, () => CpuWorker(token, seed), $"Stress CPU {seed}");
                }
            }

            if (ram)
            {
                long bytes = (long)(FreePhysicalMemory() * Math.Clamp(ramFraction, 0.1, 0.9));
                MemoryAllocatedBytes = bytes;
                StartThread(cts, () => RamWorker(token, bytes), "Stress RAM");
            }
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Thread[] threads;
        lock (_sync)
        {
            if (_cts == null) return;
            cts = _cts;
            threads = _threads.ToArray();
        }

        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        // Prazo único compartilhado entre todas as threads para evitar espera proporcional ao número de núcleos.
        var deadline = Stopwatch.StartNew();
        const int maxWaitMs = 2500;
        foreach (var thread in threads)
        {
            int remaining = maxWaitMs - (int)deadline.ElapsedMilliseconds;
            if (remaining <= 0) break;
            thread.Join(remaining);
        }
        if (threads.Any(t => t.IsAlive)) return;

        lock (_sync)
        {
            _threads.Clear();
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
                cts.Dispose();
            }
        }
        if (MemoryAllocatedBytes > 0) GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
    }

    public void Dispose() => Stop();

    private void StartThread(CancellationTokenSource cts, ThreadStart body, string name)
    {
        // BelowNormal mantém a interface responsiva sem reduzir a carga (núcleos continuam a 100%).
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex)
            {
                Error = ex.Message;
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
        })
        { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        thread.Start();
        _threads.Add(thread);
    }

    private static void CpuWorker(CancellationToken token, int seed)
    {
        // Mistura ponto flutuante (FMA/sqrt/sin) e inteiros num buffer que cabe no cache L1/L2.
        var buf = new double[4096];
        for (int i = 0; i < buf.Length; i++) buf[i] = i + seed + 1;
        ulong x = (ulong)(seed + 1) * 0x9E3779B97F4A7C15UL;

        while (!token.IsCancellationRequested)
        {
            for (int rep = 0; rep < 64; rep++)
            {
                if (token.IsCancellationRequested) return;
                for (int i = 0; i < buf.Length; i++)
                {
                    double v = buf[i];
                    v = Math.FusedMultiplyAdd(v, 1.0000001, 0.5);
                    v = Math.Sqrt(v * v + 1.0) + Math.Sin(v) * 1e-3;
                    buf[i] = v > 1e12 ? i + 1 : v;

                    x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                }
            }
            if (x == 0) buf[0] = 0; // impede que o JIT elimine o cálculo
        }
    }

    private void RamWorker(CancellationToken token, long totalBytes)
    {
        const int chunkLongs = 16 * 1024 * 1024; // 128 MB por bloco
        var chunks = new List<long[]>();
        try
        {
            long allocated = 0;
            try
            {
                while (allocated + chunkLongs * 8L <= totalBytes && !token.IsCancellationRequested)
                {
                    chunks.Add(new long[chunkLongs]);
                    allocated += chunkLongs * 8L;
                }
            }
            catch (OutOfMemoryException) { }
            MemoryAllocatedBytes = allocated;
            if (chunks.Count == 0) { Error = "Não foi possível alocar memória para o teste."; return; }

            ulong pass = 0;
            while (!token.IsCancellationRequested)
            {
                pass++;
                long pattern = unchecked((long)(pass * 0x9E3779B97F4A7C15UL));
                foreach (var c in chunks)
                {
                    for (int i = 0; i < c.Length; i++) c[i] = pattern ^ i;
                    if (token.IsCancellationRequested) return;
                }
                foreach (var c in chunks)
                {
                    long errs = 0;
                    for (int i = 0; i < c.Length; i++)
                        if (c[i] != (pattern ^ i)) errs++;
                    if (errs > 0) Interlocked.Add(ref _memErrors, errs);
                    if (token.IsCancellationRequested) return;
                }
                Interlocked.Increment(ref _memPasses);
            }
        }
        finally
        {
            chunks.Clear();
        }
    }

    private static long FreePhysicalMemory() =>
        MemoryInfo.ReadPhysicalUsage() is { Available: > 0 } usage ? usage.Available : 1L << 30;
}
