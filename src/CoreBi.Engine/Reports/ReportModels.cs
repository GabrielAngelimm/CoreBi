using System.Text.Json.Serialization;

namespace CoreBi.Engine.Reports;

public sealed record ReportNode(string Name, string? Value, List<ReportNode>? Children);
public sealed record ReportSensor(string Hardware, string Category, string Name, string Value, string Min, string Max);
public sealed record ReportBench(DateTime Date, string Test, string Summary);

/// <summary>Achado de outra página (disco, bateria, rede). Tone: "ok", "warn", "danger" ou vazio.</summary>
public sealed record ReportDisk(string Name, string Details, string Verdict, string Reasons, List<KeyValuePair<string, string>> Metrics, string Tone = "");
public sealed record ReportGpu(string Name, string Subtitle, List<ReportNode> Sections);

public sealed record ReportRow(string Key, string Value, string? Note = null, List<string>? Cells = null, string? Advice = null);
public sealed record ReportToken(string Text, bool On);
public sealed record ReportGroup(string Title, List<ReportRow> Rows, List<string>? Columns = null, List<ReportToken>? Tokens = null);
public sealed record ReportReadout(string Label, string Value, string Unit);
public sealed record ReportVolume(string Letter, string Name, string Line, double Used);
public sealed record ReportDrive(string Title, string Meta, string Size, string Health, List<ReportVolume> Volumes);

public sealed record ReportSheet(
    string Category,
    string Eyebrow,
    string Title,
    string Subtitle,
    string Summary,
    List<ReportReadout> Readouts,
    List<ReportGroup> Groups,
    List<ReportDrive>? Drives = null,
    [property: JsonIgnore] string Channel = "steel");

/// <summary>Tudo que entra no relatório exportado (HTML, PDF ou JSON).</summary>
public sealed class ReportData
{
    public DateTime Date { get; init; } = DateTime.Now;
    public string Machine { get; init; } = Environment.MachineName;
    public string AppVersion { get; init; } = "";
    public string? LogoBase64 { get; init; }
    public string? LogoOnDarkBase64 { get; init; }
    public bool Dark { get; init; }
    public List<ReportSheet> Sheets { get; init; } = new();
    public List<ReportNode> System { get; init; } = new();
    public List<ReportGpu> Gpus { get; init; } = new();
    public List<ReportDisk> Disks { get; init; } = new();
    public List<ReportDisk> Power { get; init; } = new();
    public List<ReportDisk> Network { get; init; } = new();
    public List<ReportSensor> Sensors { get; init; } = new();
    public List<ReportBench> Benchmarks { get; init; } = new();
}
