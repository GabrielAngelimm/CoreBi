namespace CoreBi.Engine.Monitoring.Health;

public enum HealthVerdict { Good, Warning, Critical, Unknown }

public static class HealthVerdictExtensions
{
    public static string ToVerdictText(this HealthVerdict verdict, string? label = null) =>
        label ?? verdict switch
        {
            HealthVerdict.Good => "SAUDÁVEL",
            HealthVerdict.Warning => "ATENÇÃO",
            HealthVerdict.Critical => "CRÍTICO",
            _ => "SEM DADOS",
        };
}

public sealed class HealthCard
{
    public string Title { get; init; } = "";
    public string Meta { get; init; } = "";
    public HealthVerdict Verdict { get; init; } = HealthVerdict.Unknown;
    public string? Label { get; init; }
    public string VerdictText => Verdict.ToVerdictText(Label);
    public List<KeyValuePair<string, string>> Metrics { get; init; } = new();
    public List<string> Reasons { get; init; } = new();
}
