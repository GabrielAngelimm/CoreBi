namespace CoreBi.Engine.Performance.Stress;

/// <summary>Como um teste de carga terminou: no tempo, parado pela pessoa ou interrompido (corte, erro de memória, falha).</summary>
public enum StressOutcome { Completed, Stopped, Aborted }

/// <summary>
/// Um teste de carga encerrado, guardado em AppSettings.StressRuns para o histórico da página: o que foi carregado, quanto
/// durou, como terminou e o que se mediu (as temperaturas máximas, as passadas e os erros de memória, o FPS da janela 3D).
/// </summary>
public sealed record StressRun
{
    public const int Capacity = 30;

    public DateTime Date { get; init; }
    public bool Cpu { get; init; }
    public bool Ram { get; init; }
    public bool Gpu { get; init; }
    /// <summary>Duração escolhida em minutos (0 = sem limite).</summary>
    public double PlannedMinutes { get; init; }
    public double Seconds { get; init; }
    public StressOutcome Outcome { get; init; }
    /// <summary>O motivo do fim em uma frase ("Tempo de teste concluído", "O processador chegou a 95 °C, no limite de 95 °C").</summary>
    public string Reason { get; init; } = "";
    public double? CpuMaxC { get; init; }
    public double? GpuMaxC { get; init; }
    public long? RamPasses { get; init; }
    public long? RamErrors { get; init; }
    public double? GpuAvgFps { get; init; }

    public static List<StressRun> Normalize(List<StressRun>? runs) =>
        (runs ?? []).Where(r => r != null && r.Seconds >= 0).OrderByDescending(r => r.Date).Take(Capacity).ToList();
}
