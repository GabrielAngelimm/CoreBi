using CoreBi.Desktop.Features.Monitoring.Inventory;
using CoreBi.Engine.Monitoring;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Reports;

namespace CoreBi.Desktop.Features.Reports;

/// <summary>Leva as fichas da página Sistema e a árvore de nós para o relatório, omitindo campos privados.</summary>
internal static class SystemReport
{
    internal static readonly string[] Order = { SystemSheets.Os, SystemSheets.Cpu, SystemSheets.Gpu, SystemSheets.Ram, SystemSheets.Board, SystemSheets.Storage };

    public static ReportNode? ToNode(InfoNode node)
    {
        string header = node.Header ?? "";
        if (ReportBuilder.IsPrivate(header)) return null;
        var children = node.Children.Select(ToNode).Where(k => k != null).Cast<ReportNode>().ToList();
        return new ReportNode(header, string.IsNullOrEmpty(node.Value) ? null : node.Value, children.Count > 0 ? children : null);
    }

    public static List<ReportSheet> From(IReadOnlyList<IndexItem> index, IReadOnlyDictionary<string, Sheet> sheets)
    {
        var list = new List<ReportSheet>();
        foreach (var key in Order)
        {
            if (!sheets.TryGetValue(key, out var s) || index.FirstOrDefault(i => i.Key == key) is not { } item) continue;
            var groups = s.Groups.Select(g => new ReportGroup(g.Title,
                    g.Rows.Where(r => !ReportBuilder.IsPrivate(r.Key)).Select(Row).ToList(),
                    g.Columns,
                    g.Tokens?.Select(t => new ReportToken(t.Text, t.On)).ToList()))
                .Where(g => g.Rows.Count > 0 || g.Tokens is { Count: > 0 }).ToList();
            var drives = s.Disks?.Select(d => new ReportDrive(d.Title, d.Meta, $"{d.Size} {d.Unit}".Trim(), d.Health,
                d.Volumes.Select(v => new ReportVolume(v.Letter, v.Name, v.Line, v.Used)).ToList())).ToList();
            list.Add(new ReportSheet(item.Category, s.Eyebrow, s.Title, s.Subtitle, item.Spec,
                s.Readouts.Select(r => new ReportReadout(r.Label, r.Value, r.Unit)).ToList(), groups, drives, Channel(item.Channel)));
        }
        return list;
    }

    private static string Channel(string channel) => channel switch
    {
        "Cpu" => "cpu",
        "Gpu" => "gpu",
        "Memory" => "memory",
        "Storage" => "storage",
        "Board" => "board",
        _ => "steel",
    };

    // A explicação só entra quando diz o que fazer (veredito âmbar); o resto é contexto da tela.
    private static ReportRow Row(SpecRow r) => new(r.Key, r.Value, r.Note, r.Cells, r.Tone == Tone.Warn ? r.Help : null);
}
