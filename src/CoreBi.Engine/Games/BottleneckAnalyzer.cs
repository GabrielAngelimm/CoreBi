using System.Diagnostics;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;

namespace CoreBi.Engine.Games;

public enum Limiter { Gpu, Cpu, Vram, Ram, Thermal, Capped, Unknown }

public sealed record LoadSample(
    float? GpuLoad, float? CpuTotal, float? CpuMaxCore, string? BusiestCore,
    double? VramPct, float? RamPct, float? GpuTemp, float? CpuTemp, bool GpuThermalCap, double? Fps,
    double? Low1Fps = null, string? Process = null)
{
    public const double VramLimitPct = 97;
    public const float ThermalLimitC = 95;
    public const float GpuLimitPct = 90;
    public const float CpuLimitPct = 85;
    public const float RamLimitPct = 92;

    /// <summary>
    /// Regras, na ordem: VRAM cheia → térmico → GPU no limite (normal em jogo pesado) → CPU/núcleo no limite com GPU
    /// sobrando → RAM cheia → nada no limite (trava de FPS, V-Sync ou menu).
    /// </summary>
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
        var cpuLive = CpuTelemetry.Read(monitor, snapshot);
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
            frames?.Fps,
            frames?.Low1Fps,
            frames?.ProcessName);
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

    /// <summary>As situações que a análise conta, na ordem da tela (o esperado primeiro, depois o que pede ação).</summary>
    public static readonly Limiter[] Situations = [Limiter.Gpu, Limiter.Cpu, Limiter.Vram, Limiter.Ram, Limiter.Thermal, Limiter.Capped];

    /// <summary>O que cada situação quer dizer, numa frase curta e com o limite que a classificação usa.</summary>
    public static string Meaning(Limiter l) => l switch
    {
        Limiter.Gpu => $"A placa de vídeo trabalha a {LoadSample.GpuLimitPct:0} % ou mais. É o normal num jogo pesado.",
        Limiter.Cpu => "O processador, ou um núcleo dele, não acompanha a placa de vídeo.",
        Limiter.Vram => $"A memória da placa de vídeo passou de {LoadSample.VramLimitPct:0} % e o jogo engasga.",
        Limiter.Ram => $"A RAM passou de {LoadSample.RamLimitPct:0} % e o Windows passa a usar o disco.",
        Limiter.Thermal => $"Uma peça chegou a {LoadSample.ThermalLimitC:0} °C ou o driver baixou o clock pelo calor.",
        Limiter.Capped => "Nenhuma peça no limite: trava de FPS, V-Sync, menu ou pausa.",
        _ => "",
    };

    /// <summary>O que fazer, numa linha só: a frase que fica na tela embaixo do veredito (o conselho inteiro fica na dica).</summary>
    public static string Hint(Limiter l) => l switch
    {
        Limiter.Unknown => "Faltam o FPS do jogo e as leituras dos sensores.",
        Limiter.Gpu => "Normal num jogo pesado. Para mais FPS, baixe a resolução ou ative DLSS ou FSR.",
        Limiter.Cpu => "Baixe distância de visão e multidões, feche programas e confira o XMP ou EXPO.",
        Limiter.Vram => "Baixe a qualidade das texturas ou a resolução.",
        Limiter.Ram => "Feche navegadores e outros programas.",
        Limiter.Thermal => "Confira a ventilação, a poeira e a pasta térmica.",
        _ => "Trava de FPS, V-Sync, menu ou pausa. Normal se o FPS já é o desejado."
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
    private readonly Dictionary<string, int> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan? _lastGame;
    public TimeSpan Duration { get; init; }
    public int Count => _samples.Count;

    /// <summary>
    /// Tempo com um jogo em primeiro plano. A análise só anda enquanto o jogo entrega quadros: com o CoreBi ou outra janela na
    /// frente (Alt+Tab, menu do Windows) o relógio para, e a duração escolhida é toda de jogo.
    /// </summary>
    public TimeSpan Measured { get; private set; }
    public bool Done => Measured >= Duration;

    public void Add(LoadSample s) => Add(s, _clock.Elapsed);

    internal void Add(LoadSample s, TimeSpan now)
    {
        if (s.Fps is not > 0)
        {
            _lastGame = null;
            return;
        }
        // Entre duas leituras com jogo; um vão maior que 2 s (tique atrasado, jogo voltando) conta só 1 s
        if (_lastGame is TimeSpan last) Measured += now - last <= TimeSpan.FromSeconds(2) ? now - last : TimeSpan.FromSeconds(1);
        _lastGame = now;
        var limiter = s.Classify();
        if (limiter == Limiter.Unknown) return;
        _samples.Add((s, limiter));
        if (!string.IsNullOrWhiteSpace(s.Process)) _processes[s.Process] = _processes.GetValueOrDefault(s.Process) + 1;
    }

    /// <summary>O programa medido na maior parte da sessão (só o nome do processo, como o resto do Gargalo).</summary>
    public string? Process => _processes.Count == 0 ? null : _processes.MaxBy(p => p.Value).Key;

    /// <summary>A sessão pronta para guardar (null sem nenhuma amostra classificada).</summary>
    public BottleneckRun? ToRun(DateTime date)
    {
        if (_samples.Count == 0) return null;
        return new BottleneckRun
        {
            Date = date,
            Process = Process ?? "",
            Seconds = Math.Round(Measured.TotalSeconds),
            Samples = Count,
            Main = Main ?? Limiter.Unknown,
            Shares = Distribution().Select(d => new BottleneckShare(d.Limiter, Math.Round(d.Pct, 1))).ToList(),
            AvgFps = AvgFps,
            AvgLow1Fps = Avg(x => x.Low1Fps is double l ? (float)l : null),
            GpuAvg = Avg(x => x.GpuLoad),
            CpuAvg = Avg(x => x.CpuTotal),
            CoreAvg = Avg(x => x.CpuMaxCore),
            VramMax = Max(x => x.VramPct),
            RamMax = Max(x => x.RamPct),
            GpuTempMax = Max(x => x.GpuTemp),
            CpuTempMax = Max(x => x.CpuTemp),
        };
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

public sealed record BottleneckShare(Limiter Limiter, double Pct);

/// <summary>
/// Uma análise de sessão concluída, guardada em AppSettings.BottleneckRuns para a página abrir com a última. Só o nome do
/// processo (nunca caminho nem PID), as médias e os máximos e o tempo em cada situação.
/// </summary>
public sealed record BottleneckRun
{
    public const int Capacity = 20;
    public DateTime Date { get; init; }
    public string Process { get; init; } = "";
    public double Seconds { get; init; }
    public int Samples { get; init; }
    public Limiter Main { get; init; } = Limiter.Unknown;
    public List<BottleneckShare> Shares { get; init; } = [];
    public double? AvgFps { get; init; }
    public double? AvgLow1Fps { get; init; }
    public double? GpuAvg { get; init; }
    public double? CpuAvg { get; init; }
    public double? CoreAvg { get; init; }
    public double? VramMax { get; init; }
    public double? RamMax { get; init; }
    public double? GpuTempMax { get; init; }
    public double? CpuTempMax { get; init; }

    public double Share(Limiter l) => Shares.FirstOrDefault(s => s.Limiter == l)?.Pct ?? 0;

    public static List<BottleneckRun> Normalize(List<BottleneckRun>? runs) =>
        (runs ?? []).Where(r => r != null && r.Samples > 0 && r.Shares != null).OrderByDescending(r => r.Date).Take(Capacity).ToList();
}
