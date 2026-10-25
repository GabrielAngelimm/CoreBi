using CoreBi.Desktop.Features.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Reports;

namespace CoreBi.Desktop.Features.Reports;

/// <summary>Leva as fichas da página Sistema para o relatório, na ordem do documento e sem números de série.</summary>
internal static class SystemReport
{
    private static readonly string[] Order = { SystemSheets.Os, SystemSheets.Cpu, SystemSheets.Gpu, SystemSheets.Ram, SystemSheets.Board, SystemSheets.Storage };

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
                s.Readouts.Select(r => new ReportReadout(r.Label, r.Value, r.Unit)).ToList(), groups, drives));
        }
        return list;
    }

    // A explicação só entra quando diz o que fazer (veredito âmbar); o resto é contexto da tela.
    private static ReportRow Row(SpecRow r) => new(r.Key, r.Value, r.Note, r.Cells, r.Tone == Tone.Warn ? r.Help : null);
}
