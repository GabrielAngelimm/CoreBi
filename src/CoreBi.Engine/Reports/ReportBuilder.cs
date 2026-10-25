using CoreBi.Engine.Monitoring.Inventory;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CoreBi.Engine.Reports;

public sealed record ReportNode(string Name, string? Value, List<ReportNode>? Children);
public sealed record ReportSensor(string Hardware, string Category, string Name, string Value, string Min, string Max);
public sealed record ReportBench(DateTime Date, string Test, string Summary);
public sealed record ReportDisk(string Name, string Details, string Verdict, string Reasons, List<KeyValuePair<string, string>> Metrics);
public sealed record ReportGpu(string Name, string Subtitle, List<ReportNode> Sections);

// Ficha técnica: o mesmo conteúdo da página Sistema, já sem números de série.

/// <summary>Linha chave/valor. Cells: uma célula por coluna do grupo (módulos de memória). Advice: o que fazer, quando há.</summary>
public sealed record ReportRow(string Key, string Value, string? Note = null, List<string>? Cells = null, string? Advice = null);
public sealed record ReportToken(string Text, bool On);
public sealed record ReportGroup(string Title, List<ReportRow> Rows, List<string>? Columns = null, List<ReportToken>? Tokens = null);
public sealed record ReportReadout(string Label, string Value, string Unit);
public sealed record ReportVolume(string Letter, string Name, string Line, double Used);
public sealed record ReportDrive(string Title, string Meta, string Size, string Health, List<ReportVolume> Volumes);

/// <summary>Uma categoria: o nome da peça, as leituras que a resumem e os grupos de dados.</summary>
public sealed record ReportSheet(string Category, string Eyebrow, string Title, string Subtitle, string Summary,
    List<ReportReadout> Readouts, List<ReportGroup> Groups, List<ReportDrive>? Drives = null);

/// <summary>Tudo que entra no relatório exportado.</summary>
public sealed class ReportData
{
    public DateTime Date { get; init; } = DateTime.Now;
    public string Machine { get; init; } = Environment.MachineName;
    public string AppVersion { get; init; } = "";
    public string? LogoBase64 { get; init; }
    public List<ReportSheet> Sheets { get; init; } = new();
    public List<ReportNode> System { get; init; } = new();
    public List<ReportGpu> Gpus { get; init; } = new();
    public List<ReportDisk> Disks { get; init; } = new();
    public List<ReportDisk> Power { get; init; } = new();
    public List<ReportDisk> Network { get; init; } = new();
    public List<ReportSensor> Sensors { get; init; } = new();
    public List<ReportBench> Benchmarks { get; init; } = new();
}

/// <summary>
/// Gera o relatório em HTML (arquivo único, pronto para imprimir ou salvar em PDF em A4) ou JSON. Números de série e o nome
/// do usuário ficam de fora: o relatório costuma ser enviado a terceiros.
/// </summary>
public static class ReportBuilder
{
    /// <summary>Chaves que não saem do computador.</summary>
    public static bool IsPrivate(string key) =>
        key.Contains("série", StringComparison.OrdinalIgnoreCase) || key.Contains("serial", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Usuário", StringComparison.OrdinalIgnoreCase);

    public static ReportNode? ToNode(InfoNode n)
    {
        string h = n.Header ?? "";
        if (IsPrivate(h)) return null;
        var kids = n.Children.Select(ToNode).Where(k => k != null).Cast<ReportNode>().ToList();
        return new ReportNode(h, string.IsNullOrEmpty(n.Value) ? null : n.Value, kids.Count > 0 ? kids : null);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Json(ReportData d) => JsonSerializer.Serialize(new
    {
        gerado_em = d.Date.ToString("s"),
        computador = d.Machine,
        versao_do_programa = d.AppVersion,
        fichas = d.Sheets,
        sistema = d.System,
        placas_de_video = d.Gpus,
        discos = d.Disks.Select(x => new
        {
            x.Name,
            x.Details,
            saude = x.Verdict,
            x.Reasons,
            metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value)
        }),
        energia = d.Power.Select(x => new { x.Name, x.Details, estado = x.Verdict, x.Reasons, metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value) }),
        rede = d.Network.Select(x => new { x.Name, x.Details, estado = x.Verdict, x.Reasons, metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value) }),
        sensores = d.Sensors,
        benchmarks = d.Benchmarks,
    }, JsonOptions);

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>"SAUDÁVEL" → "Saudável" (vereditos das outras páginas vêm em caixa alta).</summary>
    private static string Sentence(string s) =>
        s.Length > 1 && s == s.ToUpper(CultureInfo.GetCultureInfo("pt-BR")) ? s[0] + s[1..].ToLower(CultureInfo.GetCultureInfo("pt-BR")) : s;

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static string Html(ReportData d)
    {
        var sb = new StringBuilder(96 * 1024);
        string date = d.Date.ToString("dd/MM/yyyy 'às' HH:mm", Br);
        string version = d.AppVersion.Length > 0 ? $"CoreBi {d.AppVersion}" : "CoreBi";
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>{E(d.Machine)} · Relatório do sistema</title><style>{Css}");
        // Rodapé de cada página impressa (navegadores Chromium; os outros só ignoram).
        sb.Append($"@page{{@bottom-left{{content:\"{CssText(d.Machine)} · {CssText(d.Date.ToString("dd/MM/yyyy HH:mm", Br))}\"}}");
        sb.Append("@bottom-right{content:\"Página \" counter(page) \" de \" counter(pages)}}");
        sb.Append("</style></head><body><main class=\"paper\">");

        // ---------------- Capa: quem é o computador, num relance ----------------
        sb.Append("<header class=\"cover\"><div class=\"brand\">");
        if (!string.IsNullOrEmpty(d.LogoBase64)) sb.Append($"<img alt=\"\" src=\"data:image/png;base64,{d.LogoBase64}\">");
        sb.Append($"<span class=\"brand-name\">CoreBi</span><span class=\"brand-doc\">Relatório do sistema</span></div>");
        sb.Append($"<div class=\"stamp\">{E(date)}<br>{E(version)}</div></header>");
        sb.Append($"<h1>{E(d.Machine)}</h1>");
        var headline = d.Sheets.Where(s => s.Category is "Sistema operacional" or "Processador" or "Placa de vídeo" or "Memória RAM")
            .Select(s => s.Title).Where(t => t.Length > 0 && t != "—");
        sb.Append($"<p class=\"lead\">{E(string.Join("  ·  ", headline))}</p>");

        if (d.Sheets.Count > 0)
        {
            sb.Append("<nav class=\"summary\">");
            for (int i = 0; i < d.Sheets.Count; i++)
            {
                var s = d.Sheets[i];
                sb.Append($"<a href=\"#s{i + 1}\"><span class=\"n\">{i + 1:00}</span><span class=\"cat\">{E(s.Category)}</span>");
                sb.Append($"<span class=\"name\">{E(s.Title)}</span><span class=\"spec\">{E(s.Summary)}</span></a>");
            }
            sb.Append("</nav>");
        }

        // ---------------- Fichas ----------------
        for (int i = 0; i < d.Sheets.Count; i++) Sheet(sb, d.Sheets[i], i + 1);

        // ---------------- Diagnóstico: o que as outras páginas leram ----------------
        int n = d.Sheets.Count;
        bool any = d.Disks.Count + d.Power.Count + d.Network.Count + d.Sensors.Count + d.Benchmarks.Count > 0;
        if (any) sb.Append("<div class=\"part\"><span>Diagnóstico</span><p>Estado lido no momento da exportação, nas páginas Discos, Energia, Rede, Sensores e Benchmarks.</p></div>");
        if (d.Disks.Count > 0) Findings(sb, ++n, "Saúde dos discos", d.Disks);
        if (d.Power.Count > 0) Findings(sb, ++n, "Bateria e energia", d.Power);
        if (d.Network.Count > 0) Findings(sb, ++n, "Teste de rede", d.Network);
        if (d.Sensors.Count > 0) Sensors(sb, ++n, d.Sensors);
        if (d.Benchmarks.Count > 0) Benchmarks(sb, ++n, d.Benchmarks);

        sb.Append($"<footer><p>Gerado pelo {E(version)} em {E(date)}. Os dados vêm do Windows, do driver da placa de vídeo e dos sensores da placa-mãe.</p>");
        sb.Append("<p>Números de série e o nome do usuário ficam fora deste relatório.</p></footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static string CssText(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void Head(StringBuilder sb, int number, string title) =>
        sb.Append($"<div class=\"head\"><span class=\"n\">{number:00}</span><h2>{E(title)}</h2></div>");

    private static void Sheet(StringBuilder sb, ReportSheet s, int number)
    {
        sb.Append($"<section class=\"sheet\" id=\"s{number}\"><div class=\"keep\">");
        Head(sb, number, s.Category);
        sb.Append("<div class=\"plate\"><div class=\"who\">");
        if (s.Eyebrow.Length > 0) sb.Append($"<p class=\"eyebrow\">{E(s.Eyebrow)}</p>");
        sb.Append($"<p class=\"title\">{E(s.Title)}</p>");
        if (s.Subtitle.Length > 0) sb.Append($"<p class=\"sub\">{E(s.Subtitle)}</p>");
        sb.Append("</div>");
        if (s.Readouts.Count > 0)
        {
            sb.Append("<dl class=\"readouts\">");
            foreach (var r in s.Readouts)
                sb.Append($"<div><dt>{E(r.Label)}</dt><dd>{E(r.Value)}{(r.Unit.Length > 0 ? $"<small>{E(r.Unit)}</small>" : "")}</dd></div>");
            sb.Append("</dl>");
        }
        sb.Append("</div></div>");

        if (s.Drives is { Count: > 0 })
        {
            sb.Append("<div class=\"drives\">");
            foreach (var dr in s.Drives)
            {
                sb.Append($"<div class=\"drive\"><div class=\"dr-head\"><div><p class=\"dr-name\">{E(dr.Title)}</p><p class=\"dr-meta\">{E(dr.Meta)}</p></div>");
                sb.Append($"<div class=\"dr-size\">{E(dr.Size)}{(dr.Health.Length > 0 ? $"<span>{E(dr.Health)}</span>" : "")}</div></div>");
                foreach (var v in dr.Volumes)
                    sb.Append($"<div class=\"vol\"><span class=\"l\">{E(v.Letter)}</span><span class=\"vn\">{E(v.Name)}</span><span class=\"bar\"><i style=\"width:{Math.Clamp(v.Used, 0, 1) * 100:0.#}%\"></i></span><span class=\"vl\">{E(v.Line)}</span></div>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        if (s.Groups.Count > 0)
        {
            sb.Append("<div class=\"groups\">");
            foreach (var g in s.Groups) Group(sb, g);
            sb.Append("</div>");
        }
        sb.Append("</section>");
    }

    private static void Group(StringBuilder sb, ReportGroup g)
    {
        // Grupo com colunas (módulos lado a lado) ocupa a largura toda.
        sb.Append($"<div class=\"group{(g.Columns != null ? " wide" : "")}\"><h3>{E(g.Title)}</h3>");
        if (g.Rows.Count > 0)
        {
            sb.Append("<table>");
            if (g.Columns != null)
            {
                sb.Append("<thead><tr><th></th>");
                foreach (var c in g.Columns) sb.Append($"<th>{E(c)}</th>");
                sb.Append("</tr></thead>");
            }
            sb.Append("<tbody>");
            foreach (var r in g.Rows)
            {
                sb.Append($"<tr><th scope=\"row\">{E(r.Key)}</th>");
                if (r.Cells != null) foreach (var c in r.Cells) sb.Append($"<td>{E(c)}</td>");
                else
                {
                    sb.Append($"<td>{E(r.Value)}");
                    if (!string.IsNullOrEmpty(r.Note)) sb.Append($" <span class=\"note\">{E(r.Note)}</span>");
                    if (!string.IsNullOrEmpty(r.Advice)) sb.Append($"<span class=\"advice\">{E(r.Advice)}</span>");
                    sb.Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }
        if (g.Tokens is { Count: > 0 })
        {
            sb.Append("<p class=\"tokens\">");
            foreach (var t in g.Tokens) sb.Append($"<span class=\"{(t.On ? "on" : "off")}\" title=\"{(t.On ? "Suportada" : "Não suportada")}\">{E(t.Text)}</span>");
            sb.Append("</p>");
        }
        sb.Append("</div>");
    }

    private static void Findings(StringBuilder sb, int number, string title, List<ReportDisk> items)
    {
        sb.Append($"<section class=\"sheet\">");
        Head(sb, number, title);
        sb.Append("<div class=\"findings\">");
        foreach (var x in items)
        {
            sb.Append($"<div class=\"finding\"><div class=\"f-head\"><p class=\"f-name\">{E(x.Name)}</p><p class=\"f-verdict\">{E(Sentence(x.Verdict))}</p></div>");
            if (x.Details.Length > 0) sb.Append($"<p class=\"f-meta\">{E(x.Details)}</p>");
            if (x.Reasons.Length > 0) sb.Append($"<p class=\"f-text\">{E(x.Reasons)}</p>");
            if (x.Metrics.Count > 0)
            {
                sb.Append("<table><tbody>");
                foreach (var m in x.Metrics.Where(m => !IsPrivate(m.Key)))
                    sb.Append($"<tr><th scope=\"row\">{E(m.Key)}</th><td>{E(m.Value)}</td></tr>");
                sb.Append("</tbody></table>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div></section>");
    }

    private static void Sensors(StringBuilder sb, int number, List<ReportSensor> sensors)
    {
        sb.Append("<section class=\"sheet\">");
        Head(sb, number, "Sensores");
        sb.Append("<p class=\"intro\">Leitura no momento da exportação, com o mínimo e o máximo desde que o CoreBi abriu.</p><div class=\"sensors\">");
        foreach (var hw in sensors.GroupBy(s => s.Hardware))
        {
            sb.Append($"<div class=\"group wide\"><h3>{E(hw.Key)}</h3><table class=\"num\"><thead><tr><th></th><th>Agora</th><th>Mín.</th><th>Máx.</th></tr></thead><tbody>");
            foreach (var cat in hw.GroupBy(s => s.Category))
            {
                sb.Append($"<tr class=\"cat\"><td colspan=\"4\">{E(Sentence(cat.Key))}</td></tr>");
                foreach (var s in cat)
                    sb.Append($"<tr><th scope=\"row\">{E(s.Name)}</th><td>{E(s.Value)}</td><td class=\"dim\">{E(s.Min)}</td><td class=\"dim\">{E(s.Max)}</td></tr>");
            }
            sb.Append("</tbody></table></div>");
        }
        sb.Append("</div></section>");
    }

    private static void Benchmarks(StringBuilder sb, int number, List<ReportBench> list)
    {
        sb.Append("<section class=\"sheet\">");
        Head(sb, number, "Benchmarks e testes");
        sb.Append("<div class=\"group wide\"><table class=\"bench\"><thead><tr><th>Data</th><th>Teste</th><th>Resultado</th></tr></thead><tbody>");
        foreach (var b in list)
            sb.Append($"<tr><td class=\"dim\">{b.Date.ToString("dd/MM/yyyy HH:mm", Br)}</td><td>{E(b.Test)}</td><td>{E(b.Summary)}</td></tr>");
        sb.Append("</tbody></table></div></section>");
    }

    // Documento claro e sóbrio (é para imprimir e arquivar), com o verde-água e a Bahnschrift do aplicativo nos números.
    private const string Css = @"
:root{--paper:#fff;--desk:#e9eef0;--ink:#0f1a20;--muted:#56666f;--faint:#8b989f;--line:#e2e8eb;--line2:#cfd8dc;--accent:#087e78;--accent-soft:#e7f3f2;--warn:#9a5a00}
*{box-sizing:border-box}
html{background:var(--desk)}
body{margin:0;color:var(--ink);font:13px/1.5 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif;-webkit-print-color-adjust:exact;print-color-adjust:exact}
.paper{max-width:900px;margin:32px auto;background:var(--paper);padding:56px 60px 44px;border-radius:6px;box-shadow:0 1px 2px rgba(15,26,32,.06),0 12px 40px rgba(15,26,32,.08)}
p{margin:0}
.num-font,.readouts dd,.summary .n,.head .n,h1,.dr-size{font-family:Bahnschrift,'Segoe UI Variable Display','Segoe UI',sans-serif;font-variant-numeric:tabular-nums}

.cover{display:flex;justify-content:space-between;align-items:flex-start;padding-bottom:18px;border-bottom:1px solid var(--line)}
.brand{display:flex;align-items:center;gap:10px}.brand img{width:26px;height:26px}
.brand-name{font-weight:700;font-size:14px;letter-spacing:.2px}
.brand-doc{color:var(--muted);font-size:12.5px;padding-left:10px;border-left:1px solid var(--line2)}
.stamp{color:var(--muted);font-size:12px;text-align:right;line-height:1.55}
h1{margin:30px 0 4px;font-size:40px;font-weight:600;letter-spacing:-.3px;line-height:1.1}
.lead{color:var(--muted);font-size:14px}

.summary{display:grid;grid-template-columns:repeat(3,1fr);gap:1px;background:var(--line);border:1px solid var(--line);border-radius:8px;overflow:hidden;margin:26px 0 8px}
.summary a{display:grid;grid-template-columns:auto 1fr;column-gap:10px;padding:14px 16px;background:var(--paper);color:inherit;text-decoration:none}
.summary a:hover{background:#f6f9fa}
.summary .n{grid-row:span 3;color:var(--accent);font-size:13px;font-weight:600;padding-top:1px}
.summary .cat{color:var(--muted);font-size:11.5px}
.summary .name{font-weight:600;font-size:13.5px;line-height:1.35;margin-top:1px}
.summary .spec{color:var(--muted);font-size:12px;margin-top:2px}

.sheet{margin-top:42px}
.head{display:flex;align-items:baseline;gap:12px;padding-bottom:9px;border-bottom:2px solid var(--ink);margin-bottom:18px}
.head .n{color:var(--accent);font-size:15px;font-weight:600}
.head h2{margin:0;font-size:19px;font-weight:600;letter-spacing:-.1px}
.plate{display:flex;justify-content:space-between;align-items:flex-end;gap:28px;padding-bottom:18px;border-bottom:1px solid var(--line);margin-bottom:20px}
.eyebrow{color:var(--muted);font-size:12px;font-weight:600}
.title{font-size:22px;font-weight:600;line-height:1.2;margin-top:2px}
.sub{color:var(--muted);font-size:13px;margin-top:4px}
.readouts{display:flex;margin:0;flex-shrink:0}
.readouts div{padding:0 0 0 20px;margin-left:20px;border-left:1px solid var(--line)}
.readouts div:first-child{padding-left:0;margin-left:0;border-left:0}
.readouts dt{color:var(--muted);font-size:11.5px;white-space:nowrap}
.readouts dd{margin:2px 0 0;font-size:24px;font-weight:300;line-height:1.1;white-space:nowrap}
.readouts small{font-size:11.5px;color:var(--muted);margin-left:3px;font-family:'Segoe UI',sans-serif;font-weight:400}

.groups,.sensors{column-count:2;column-gap:40px}
.group{break-inside:avoid;margin:0 0 22px}
.group:last-child{margin-bottom:0}
.group.wide{column-span:all;display:block;padding-top:22px}
.group h3{margin:0 0 6px;font-size:13px;font-weight:600}
table{width:100%;border-collapse:collapse}
th,td{text-align:left;vertical-align:top;padding:6px 0;border-top:1px solid var(--line);font-weight:400}
tbody th{color:var(--muted);width:42%;padding-right:14px}
.wide tbody th{width:24%}
thead th{color:var(--muted);font-size:11.5px;font-weight:600;border-top:0;padding-bottom:4px}
td+td{padding-left:14px}
.note{color:var(--muted)}
.advice{display:block;color:var(--warn);font-style:italic;font-size:12px;margin-top:2px}
.tokens{display:flex;flex-wrap:wrap;gap:4px 14px;padding-top:8px;border-top:1px solid var(--line);font-size:12.5px;font-weight:600}
.tokens .off{color:var(--faint);font-weight:400;text-decoration:line-through;text-decoration-color:var(--line2)}

.drives{margin-bottom:22px}
.drive{padding:12px 0 14px;border-bottom:1px solid var(--line);break-inside:avoid}
.drive:first-child{padding-top:0}
.dr-head{display:flex;justify-content:space-between;gap:20px;margin-bottom:10px}
.dr-name{font-weight:600;font-size:13.5px}.dr-meta{color:var(--muted);font-size:12px}
.dr-size{font-size:20px;font-weight:300;text-align:right;line-height:1.1}.dr-size span{display:block;font:12px 'Segoe UI',sans-serif;color:var(--muted);margin-top:3px}
.vol{display:grid;grid-template-columns:28px minmax(120px,1fr) 2fr auto;gap:12px;align-items:center;font-size:12.5px;padding:3px 0}
.vol .l{font-weight:600}.vol .vn,.vol .vl{color:var(--muted)}
.bar{height:6px;background:var(--line);border-radius:3px;overflow:hidden}.bar i{display:block;height:100%;background:var(--accent)}

.part{margin-top:52px;padding:16px 18px;background:var(--accent-soft);border-radius:8px;break-after:avoid}
.part span{font-weight:600;font-size:14px;color:var(--accent)}.part p{color:var(--muted);font-size:12.5px;margin-top:2px}
.findings{display:grid;grid-template-columns:1fr 1fr;gap:18px 40px}
.finding{break-inside:avoid}
.f-head{display:flex;justify-content:space-between;gap:16px;align-items:baseline}
.f-name{font-weight:600}.f-verdict{font-weight:600;color:var(--accent);white-space:nowrap}
.f-meta{color:var(--muted);font-size:12px}.f-text{margin:6px 0 8px;font-size:12.5px}
.intro{color:var(--muted);font-size:12.5px;margin:-6px 0 14px}
table.num td{font-variant-numeric:tabular-nums;white-space:nowrap}
table.num thead th:not(:first-child),table.num td{text-align:right}
tr.cat td{color:var(--ink);font-weight:600;font-size:12px;padding-top:10px;text-align:left!important}
.dim{color:var(--muted)}
table.bench th:first-child{width:22%}

footer{margin-top:48px;padding-top:14px;border-top:1px solid var(--line);color:var(--muted);font-size:11.5px;line-height:1.6}

@media (max-width:720px){.paper{margin:0;padding:28px 18px;border-radius:0}.summary{grid-template-columns:1fr}.plate{flex-direction:column;align-items:flex-start}.readouts{flex-wrap:wrap;gap:10px 0}.groups,.sensors{column-count:1}.findings{grid-template-columns:1fr}h1{font-size:30px}}

@page{size:A4;margin:16mm 15mm 18mm}
@page{@bottom-left{font:9pt 'Segoe UI',sans-serif;color:#8b989f}@bottom-right{font:9pt 'Segoe UI',sans-serif;color:#8b989f}}
@media print{
html{background:#fff}body{font-size:9.5pt}
.paper{max-width:none;margin:0;padding:0;box-shadow:none;border-radius:0}
h1{margin-top:22px}
.sheet{margin-top:26px}
.keep,.head,.plate{break-inside:avoid;break-after:avoid}
.summary a:hover{background:var(--paper)}
.part{break-before:page;margin-top:0}
footer{break-inside:avoid;break-before:avoid;margin-top:28px}
}
";
}
