namespace CoreBi.Engine.Monitoring.Health;

public enum HealthVerdict { Good, Warning, Critical, Unknown }

public sealed class HealthCard
{
    public string Title { get; init; } = "";
    public string Meta { get; init; } = "";
    public HealthVerdict Verdict { get; init; } = HealthVerdict.Unknown;
    public string? Label { get; init; }
    public string VerdictText => Label ?? Verdict switch
    {
        HealthVerdict.Good => "SAUDÁVEL",
        HealthVerdict.Warning => "ATENÇÃO",
        HealthVerdict.Critical => "CRÍTICO",
        _ => "SEM DADOS"
    };
    public List<KeyValuePair<string, string>> Metrics { get; init; } = new();
    public List<string> Reasons { get; init; } = new();
}
