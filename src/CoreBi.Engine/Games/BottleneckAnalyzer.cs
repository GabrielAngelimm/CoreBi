using System.Diagnostics;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;

namespace CoreBi.Engine.Games;

public enum Limiter { Gpu, Cpu, Vram, Ram, Thermal, Capped, Unknown }

public sealed record LoadSample(
    float? GpuLoad, float? CpuTotal, float? CpuMaxCore, string? BusiestCore,
    double? VramPct, float? RamPct, float? GpuTemp, float? CpuTemp, bool GpuThermalCap, double? Fps)
{
    public const double VramLimitPct = 97;
    public const float ThermalLimitC = 95;
    public const float GpuLimitPct = 90;
    public const float CpuLimitPct = 85;
    public const float RamLimitPct = 92;

    public Limiter Classify()
    {
        if (Fps is not > 0) return Limiter.Unknown;
        if (VramPct >= VramLimitPct) return Limiter.Vram;
        if (GpuThermalCap || CpuTemp >= ThermalLimitC || GpuTemp >= ThermalLimitC) return Limiter.Thermal;
        if (GpuLoad >= GpuLimitPct) return Limiter.Gpu;
        if (CpuMaxCore >= CpuLimitPct || CpuTotal >= CpuLimitPct) return Limiter.Cpu;
        if (RamPct >= RamLimitPct) return Limiter.Ram;
        return GpuLoad != null && CpuTotal != null ? Limiter.Capped : Limiter.Unknown;
    }
}

public static class BottleneckAnalyzer
{
    public static LoadSample Sample(SensorMonitor monitor, GpuInfo? gpu, FrameStats? frames) =>
        Sample(monitor, gpu, frames, MemoryInfo.ReadPhysicalUsage());

    internal static LoadSample Sample(SensorMonitor monitor, GpuInfo? gpu, FrameStats? frames, MemoryInfo.PhysicalUsage? memory)
    {
        var snapshot = monitor.Snapshot();
        var gpuLive = gpu != null ? GpuMonitor.ReadLive(gpu, snapshot) : null;
        var cpuLive = GpuMonitor.ReadCpu(monitor, snapshot);
        return new LoadSample(
            gpuLive?.LoadPct,
            cpuLive.LoadPct,
            cpuLive.BusiestCoreLoadPct,
            cpuLive.BusiestCoreLabel,
            gpuLive?.VramPct,
            memory?.UsedPercent,
            gpuLive?.TempC,
            cpuLive.TempC,
            gpuLive?.ThermalThrottled == true,
            frames?.Fps);
    }

    public static string Title(Limiter l) => l switch
    {
        Limiter.Unknown => "Dados insuficientes",
        Limiter.Gpu => "GPU no limite",
        Limiter.Cpu => "Gargalo de CPU",
        Limiter.Vram => "Memória de vídeo cheia",
        Limiter.Ram => "Memória RAM cheia",
        Limiter.Thermal => "Temperatura no limite",
        _ => "Nada no limite"
    };

    public static string Advice(Limiter l) => l switch
    {
        Limiter.Unknown => "Sem o FPS de um jogo em primeiro plano e as leituras dos sensores, não há como classificar.",
        Limiter.Gpu => "Normal em jogos pesados: a placa de vídeo está sendo bem aproveitada. Para mais FPS, reduza a resolução " +
                       "ou a qualidade gráfica, ou ative DLSS ou FSR.",
        Limiter.Cpu => "A CPU, ou um núcleo dela, não acompanha a placa de vídeo. Reduza o que pesa nela (distância de visão, " +
                       "multidões, física), feche programas em segundo plano e confira se o XMP ou EXPO da RAM está ativo na BIOS.",
        Limiter.Vram => "A memória da placa de vídeo encheu, e isso causa travadas. Reduza a qualidade das texturas ou a resolução.",
        Limiter.Ram => "Pouca RAM livre: o Windows passa a usar o disco e o jogo engasga. Feche navegadores e outros programas.",
        Limiter.Thermal => "A temperatura alta está derrubando o clock. Verifique a ventilação do gabinete, a poeira e a pasta térmica.",
        _ => "Nem a CPU nem a placa de vídeo estão no limite. Provavelmente há trava de FPS ou V-Sync, ou o jogo está em menu " +
             "ou pausa. Normal se o FPS já é o desejado."
    };

    /// <summary>Equilíbrio teórico pelo desempenho relativo (GPU: RTX 4070 = 100; CPU: Ryzen 7 7700X = 100).</summary>
    public static (double Ratio, string Text) Balance(double gpuIdx, double cpuIdx)
    {
        if (!double.IsFinite(gpuIdx) || !double.IsFinite(cpuIdx) || gpuIdx <= 0 || cpuIdx <= 0)
            return (1, "Estimativa indisponível: o processador ou a placa de vídeo não está na tabela de desempenho relativo.");
        double ratio = gpuIdx / cpuIdx;
        string text = ratio switch
        {
            > 1.8 => "GPU muito mais forte que a CPU: em boa parte dos jogos a CPU vai limitar o FPS, principalmente em 1080p.",
            > 1.3 => "GPU um pouco à frente da CPU: jogos competitivos em 1080p tendem a ficar limitados pela CPU; " +
                     "em 1440p/4K a GPU volta a ser o limite.",
            >= 0.7 => "Hardware equilibrado: na maioria dos jogos a GPU será o limite, que é o cenário ideal.",
            > 0.4 => "CPU mais forte que a GPU: a placa de vídeo é o limite. Upgrade de GPU traria o maior ganho.",
            _ => "GPU muito fraca para esta CPU: a placa de vídeo limita quase tudo."
        };
        return (ratio, text);
    }
}

public sealed class BottleneckSession
{
    private readonly List<(LoadSample Sample, Limiter Limiter)> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public TimeSpan Duration { get; init; }
    public int Count => _samples.Count;
    public bool Done => _clock.Elapsed >= Duration;
    public TimeSpan Elapsed => _clock.Elapsed;

    public void Add(LoadSample s)
    {
        if (s.Fps is not > 0) return;
        var limiter = s.Classify();
        if (limiter != Limiter.Unknown) _samples.Add((s, limiter));
    }

    public IReadOnlyList<(Limiter Limiter, double Pct)> Distribution() =>
        _samples.GroupBy(x => x.Limiter)
                .Select(g => (g.Key, 100.0 * g.Count() / Math.Max(1, _samples.Count)))
                .OrderByDescending(x => x.Item2).ToList();

    public Limiter? Main => _samples.Count == 0 ? null : Distribution()[0].Limiter;

    public double? AvgFps
    {
        get
        {
            var f = _samples.Where(x => x.Sample.Fps > 0).Select(x => x.Sample.Fps!.Value).ToList();
            return f.Count > 0 ? f.Average() : null;
        }
    }

    public double? Avg(Func<LoadSample, float?> sel)
    {
        var v = _samples.Select(x => sel(x.Sample)).Where(x => x != null).Select(x => (double)x!.Value).ToList();
        return v.Count > 0 ? v.Average() : null;
    }

    public double? Max(Func<LoadSample, double?> sel)
    {
        var v = _samples.Select(x => sel(x.Sample)).Where(x => x != null).Select(x => x!.Value).ToList();
        return v.Count > 0 ? v.Max() : null;
    }
}
