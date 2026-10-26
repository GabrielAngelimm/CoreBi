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
    /// <summary>Logo para fundo claro (o papel do relatório e a impressão).</summary>
    public string? LogoBase64 { get; init; }
    /// <summary>Logo para fundo escuro, usado no tema escuro da tela.</summary>
    public string? LogoOnDarkBase64 { get; init; }
    /// <summary>Abre no tema escuro (o mesmo do aplicativo); quem lê pode alternar.</summary>
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

/// <summary>
/// Gera o relatório em HTML (arquivo único, com tema claro e escuro, pronto para imprimir ou salvar em PDF em A4) ou JSON. Números de série e o nome
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
        sb.Append($"<!doctype html><html lang=\"pt-BR\" data-theme=\"{(d.Dark ? "dark" : "light")}\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"color-scheme\" content=\"light dark\">");
        sb.Append($"<title>{E(d.Machine)} · Relatório do sistema</title><style>{Css}");
        // Cabeçalho e rodapé de cada folha impressa (navegadores Chromium; os outros só ignoram). A capa não repete o cabeçalho.
        sb.Append($"@page{{@top-left{{content:\"CoreBi · Relatório do sistema\"}}@top-right{{content:\"{CssText(d.Machine)}\"}}");
        sb.Append($"@bottom-left{{content:\"Gerado em {CssText(date)}\"}}@bottom-right{{content:\"Página \" counter(page) \" de \" counter(pages)}}}}");
        sb.Append("</style></head><body>");

        // Só na tela: alternar o tema e imprimir (a impressão sai sempre clara).
        sb.Append("<div class=\"tools\" role=\"toolbar\" aria-label=\"Relatório\">");
        sb.Append($"<button type=\"button\" id=\"theme\" aria-pressed=\"false\">{IconMoon}{IconSun}<span>Tema escuro</span></button>");
        sb.Append($"<button type=\"button\" id=\"print\" class=\"primary\">{IconPrint}<span>Imprimir ou salvar PDF</span></button></div>");

        sb.Append("<main class=\"paper\">");

        // ---------------- Capa: quem é o computador, num relance ----------------
        sb.Append("<header class=\"cover\"><div class=\"brand\">");
        if (!string.IsNullOrEmpty(d.LogoBase64)) sb.Append($"<img class=\"logo on-light\" alt=\"\" src=\"data:image/png;base64,{d.LogoBase64}\">");
        if (!string.IsNullOrEmpty(d.LogoOnDarkBase64)) sb.Append($"<img class=\"logo on-dark\" alt=\"\" src=\"data:image/png;base64,{d.LogoOnDarkBase64}\">");
        sb.Append("<div><p class=\"brand-name\">CoreBi</p><p class=\"brand-doc\">Relatório técnico do sistema</p></div></div>");
        sb.Append($"<dl class=\"stamp\"><div><dt>Gerado em</dt><dd>{E(date)}</dd></div><div><dt>Versão</dt><dd>{E(version)}</dd></div></dl></header>");
        sb.Append($"<div class=\"hero\"><p class=\"kicker\">Computador</p><h1>{E(d.Machine)}</h1>");
        var headline = d.Sheets.Where(s => s.Category is "Sistema operacional" or "Processador" or "Placa de vídeo" or "Memória RAM")
            .Select(s => s.Title).Where(t => t.Length > 0 && t != "—");
        sb.Append($"<p class=\"lead\">{string.Join("<i>·</i>", headline.Select(E))}</p></div>");

        if (d.Sheets.Count > 0)
        {
            sb.Append("<nav class=\"summary\" aria-label=\"Sumário\">");
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
        if (any) sb.Append("<div class=\"part\"><p class=\"part-title\">Diagnóstico</p><p>Estado lido no momento da exportação, nas páginas Discos, Energia, Rede, Sensores e Benchmarks.</p></div>");
        if (d.Disks.Count > 0) Findings(sb, ++n, "Saúde dos discos", d.Disks);
        if (d.Power.Count > 0) Findings(sb, ++n, "Bateria e energia", d.Power);
        if (d.Network.Count > 0) Findings(sb, ++n, "Teste de rede", d.Network);
        if (d.Sensors.Count > 0) Sensors(sb, ++n, d.Sensors);
        if (d.Benchmarks.Count > 0) Benchmarks(sb, ++n, d.Benchmarks);

        sb.Append("<footer><div class=\"brand small\">");
        if (!string.IsNullOrEmpty(d.LogoBase64)) sb.Append($"<img class=\"logo on-light\" alt=\"\" src=\"data:image/png;base64,{d.LogoBase64}\">");
        if (!string.IsNullOrEmpty(d.LogoOnDarkBase64)) sb.Append($"<img class=\"logo on-dark\" alt=\"\" src=\"data:image/png;base64,{d.LogoOnDarkBase64}\">");
        sb.Append($"<p>Gerado pelo {E(version)} em {E(date)}. Os dados vêm do Windows, do driver da placa de vídeo e dos sensores da placa-mãe. ");
        sb.Append("Números de série e o nome do usuário ficam fora deste relatório.</p></div></footer>");
        sb.Append($"</main><script>{Script}</script></body></html>");
        return sb.ToString();
    }

    private static string CssText(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void Head(StringBuilder sb, int number, string title) =>
        sb.Append($"<div class=\"head\"><span class=\"n\">{number:00}</span><h2>{E(title)}</h2></div>");

    private static void Sheet(StringBuilder sb, ReportSheet s, int number)
    {
        sb.Append($"<section class=\"sheet\" id=\"s{number}\"><div class=\"keep\">");
        Head(sb, number, s.Category);
        // A placa é o resumo da peça (como o cabeçalho da aba); os grupos abaixo trazem a ficha completa.
        sb.Append("<div class=\"plate\"><div class=\"who\">");
        if (s.Eyebrow.Length > 0) sb.Append($"<p class=\"eyebrow\">{E(s.Eyebrow)}</p>");
        sb.Append($"<p class=\"title\">{E(s.Title)}</p>");
        if (s.Subtitle.Length > 0) sb.Append($"<p class=\"sub\">{E(s.Subtitle)}</p>");
        sb.Append("</div>");
        if (s.Readouts.Count > 0)
        {
            sb.Append($"<dl class=\"readouts\" style=\"--n:{s.Readouts.Count}\">");
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
                    sb.Append($"<div class=\"vol\"><span class=\"l\">{E(v.Letter)}</span><span class=\"vn\">{E(v.Name)}</span><span class=\"bar\"><i style=\"width:{(Math.Clamp(v.Used, 0, 1) * 100).ToString("0.#", CultureInfo.InvariantCulture)}%\"></i></span><span class=\"vl\">{E(v.Line)}</span></div>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        // Grupos com colunas (módulos lado a lado) e de fichas (instruções) ficam na largura toda, depois das duas colunas.
        var flow = s.Groups.Where(g => g.Columns == null).ToList();
        if (flow.Count > 0)
        {
            sb.Append("<div class=\"groups\">");
            foreach (var g in flow) Group(sb, g);
            sb.Append("</div>");
        }
        foreach (var g in s.Groups.Where(g => g.Columns != null)) Group(sb, g);
        sb.Append("</section>");
    }

    private static void Group(StringBuilder sb, ReportGroup g)
    {
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
            sb.Append("</p><p class=\"legend\">Riscadas: o processador não tem.</p>");
        }
        sb.Append("</div>");
    }

    private static void Findings(StringBuilder sb, int number, string title, List<ReportDisk> items)
    {
        sb.Append("<section class=\"sheet\">");
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
        sb.Append("<p class=\"intro\">Leitura no momento da exportação, com o mínimo e o máximo desde que o CoreBi abriu.</p>");
        foreach (var hw in sensors.GroupBy(s => s.Hardware))
        {
            sb.Append($"<div class=\"group wide\"><h3>{E(hw.Key)}</h3><table class=\"num\"><thead><tr><th></th><th>Agora</th><th>Mín.</th><th>Máx.</th></tr></thead>");
            // Cada categoria é um bloco: na impressão ela não se parte entre folhas quando cabe numa só.
            foreach (var cat in hw.GroupBy(s => s.Category))
            {
                sb.Append($"<tbody><tr class=\"cat\"><td colspan=\"4\">{E(Sentence(cat.Key))}</td></tr>");
                foreach (var s in cat)
                    sb.Append($"<tr><th scope=\"row\">{E(s.Name)}</th><td>{E(s.Value)}</td><td class=\"dim\">{E(s.Min)}</td><td class=\"dim\">{E(s.Max)}</td></tr>");
                sb.Append("</tbody>");
            }
            sb.Append("</table></div>");
        }
        sb.Append("</section>");
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

    private const string IconMoon = "<svg class=\"i-moon\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z\"/></svg>";
    private const string IconSun = "<svg class=\"i-sun\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4\"/></svg>";
    private const string IconPrint = "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M7 9V3h10v6M7 17H5a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2\"/><path d=\"M7 14h10v7H7z\"/></svg>";

    // Alterna o tema da tela e abre a impressão. Sem dependências; sem JavaScript o relatório continua legível.
    private const string Script = @"(function(){var r=document.documentElement,t=document.getElementById('theme'),p=document.getElementById('print');
function s(){var d=r.getAttribute('data-theme')==='dark';t.setAttribute('aria-pressed',d?'true':'false');t.lastChild.textContent=d?'Tema claro':'Tema escuro';}
t.addEventListener('click',function(){r.setAttribute('data-theme',r.getAttribute('data-theme')==='dark'?'light':'dark');s();});
p.addEventListener('click',function(){window.print();});s();})();";

    // Documento sóbrio (é para imprimir e arquivar), com o verde-água e a Bahnschrift do aplicativo nos números.
    // Na tela há tema claro e escuro; a impressão usa sempre o claro.
    private const string Css = @"
:root{--paper:#fff;--desk:#e9eef0;--ink:#0f1a20;--muted:#55656e;--faint:#8b989f;--line:#e3e9ec;--line2:#cfd8dc;
--accent:#087e78;--accent-soft:#e8f3f2;--plate:#f3f8f8;--plate-line:#dcebea;--warn:#9a5a00;--bar:#e3e9ec;--shadow:0 1px 2px rgba(15,26,32,.06),0 12px 40px rgba(15,26,32,.08)}
@media screen{:root[data-theme=dark]{color-scheme:dark;--paper:#141d22;--desk:#0b1114;--ink:#e6edf0;--muted:#9babb3;--faint:#6d7d85;--line:#233038;--line2:#33434c;
--accent:#3cc2b7;--accent-soft:#102a2a;--plate:#18262b;--plate-line:#22393b;--warn:#e3a54e;--bar:#2a3840;--shadow:0 1px 2px rgba(0,0,0,.4),0 12px 40px rgba(0,0,0,.35)}}
*{box-sizing:border-box}
html{background:var(--desk)}
body{margin:0;color:var(--ink);font:13px/1.5 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif;-webkit-print-color-adjust:exact;print-color-adjust:exact}
.paper{max-width:920px;margin:32px auto;background:var(--paper);padding:52px 60px 40px;border-radius:8px;box-shadow:var(--shadow)}
p{margin:0}
.readouts dd,.summary .n,.head .n,h1,.dr-size{font-family:Bahnschrift,'Segoe UI Variable Display','Segoe UI',sans-serif;font-variant-numeric:tabular-nums}

.tools{position:fixed;top:16px;right:16px;z-index:5;display:flex;gap:6px;padding:5px;background:var(--paper);border:1px solid var(--line2);border-radius:12px;box-shadow:var(--shadow)}
.tools button{display:inline-flex;align-items:center;gap:7px;height:34px;padding:0 13px 0 10px;border:0;border-radius:8px;background:transparent;color:var(--ink);font:600 12.5px 'Segoe UI',system-ui,sans-serif;cursor:pointer}
.tools button:hover{background:var(--accent-soft)}
.tools button:focus-visible{outline:2px solid var(--accent);outline-offset:1px}
.tools button.primary{background:var(--accent);color:#fff}.tools button.primary:hover{filter:brightness(1.08)}
:root[data-theme=dark] .tools button.primary{color:#06201e}
.tools svg{width:17px;height:17px;fill:none;stroke:currentColor;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round}
.tools .i-sun,:root[data-theme=dark] .tools .i-moon{display:none}:root[data-theme=dark] .tools .i-sun{display:block}

.logo{display:block;width:46px;height:46px}.on-dark{display:none}
@media screen{:root[data-theme=dark] .on-light{display:none}:root[data-theme=dark] .on-dark{display:block}}
.cover{display:flex;justify-content:space-between;align-items:center;gap:24px;padding-bottom:22px;border-bottom:1px solid var(--line2);position:relative}
.cover::after{content:'';position:absolute;left:0;bottom:-2px;width:64px;height:3px;background:var(--accent)}
.brand{display:flex;align-items:center;gap:14px}
.brand-name{font:700 21px/1.1 Bahnschrift,'Segoe UI',sans-serif;letter-spacing:.3px}
.brand-doc{color:var(--muted);font-size:12.5px;margin-top:3px}
.stamp{display:flex;gap:28px;margin:0;text-align:right}
.stamp dt{color:var(--faint);font-size:11px}.stamp dd{margin:1px 0 0;font-size:12.5px;font-weight:600}
.hero{padding:34px 0 0}
.kicker{color:var(--accent);font-size:11.5px;font-weight:700;letter-spacing:1.2px;text-transform:uppercase}
h1{margin:4px 0 8px;font-size:44px;font-weight:600;letter-spacing:-.4px;line-height:1.05}
.lead{color:var(--muted);font-size:14.5px}.lead i{font-style:normal;color:var(--faint);padding:0 9px}

.summary{display:grid;grid-template-columns:repeat(3,1fr);gap:1px;background:var(--line);border:1px solid var(--line);border-radius:10px;overflow:hidden;margin:28px 0 6px}
.summary a{display:grid;grid-template-columns:auto 1fr;column-gap:10px;padding:14px 16px;background:var(--paper);color:inherit;text-decoration:none}
.summary a:hover{background:var(--plate)}
.summary .n{grid-row:span 3;color:var(--accent);font-size:13px;font-weight:600;padding-top:1px}
.summary .cat{color:var(--muted);font-size:11.5px}
.summary .name{font-weight:600;font-size:13.5px;line-height:1.35;margin-top:1px}
.summary .spec{color:var(--muted);font-size:12px;margin-top:2px}

.sheet{margin-top:44px;scroll-margin-top:24px}
.head{display:flex;align-items:baseline;gap:12px;padding-bottom:9px;border-bottom:1px solid var(--line2);margin-bottom:16px;position:relative}
.head::after{content:'';position:absolute;left:0;bottom:-2px;width:40px;height:3px;background:var(--accent)}
.head .n{color:var(--accent);font-size:15px;font-weight:600}
.head h2{margin:0;font-size:19px;font-weight:600;letter-spacing:-.1px}
.plate{background:var(--plate);border:1px solid var(--plate-line);border-radius:10px;padding:16px 20px 15px;margin-bottom:22px}
.eyebrow{color:var(--accent);font-size:11.5px;font-weight:700;letter-spacing:.4px}
.title{font-size:22px;font-weight:600;line-height:1.2;margin-top:2px}
.sub{color:var(--muted);font-size:12.5px;margin-top:3px}
.readouts{display:grid;grid-template-columns:repeat(var(--n),minmax(max-content,1fr));margin:14px 0 0;padding-top:12px;border-top:1px solid var(--plate-line)}
.readouts div{padding:0 14px;border-left:1px solid var(--plate-line)}
.readouts div:first-child{padding-left:0;border-left:0}
.readouts dt{color:var(--muted);font-size:11.5px;white-space:nowrap}
.readouts dd{margin:2px 0 0;font-size:23px;font-weight:300;line-height:1.15;white-space:nowrap}
.readouts small{font-size:11.5px;color:var(--muted);margin-left:3px;font-family:'Segoe UI',sans-serif;font-weight:400}

.groups{column-count:2;column-gap:40px}
.group{break-inside:avoid;margin:0 0 22px}
.group.wide{margin-top:24px}
.group h3{margin:0 0 5px;font-size:13px;font-weight:600}
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
.legend{color:var(--faint);font-size:11.5px;margin-top:4px}

.drives{margin-bottom:22px}
.drive{padding:12px 0 14px;border-bottom:1px solid var(--line);break-inside:avoid}
.drive:first-child{padding-top:0}
.dr-head{display:flex;justify-content:space-between;gap:20px;margin-bottom:10px}
.dr-name{font-weight:600;font-size:13.5px}.dr-meta{color:var(--muted);font-size:12px}
.dr-size{font-size:20px;font-weight:300;text-align:right;line-height:1.1}.dr-size span{display:block;font:12px 'Segoe UI',sans-serif;color:var(--muted);margin-top:3px}
.vol{display:grid;grid-template-columns:28px minmax(120px,1fr) 2fr auto;gap:12px;align-items:center;font-size:12.5px;padding:3px 0}
.vol .l{font-weight:600}.vol .vn,.vol .vl{color:var(--muted)}
.bar{height:6px;background:var(--bar);border-radius:3px;overflow:hidden}.bar i{display:block;height:100%;background:var(--accent)}

.part{margin-top:56px;padding:16px 20px;background:var(--accent-soft);border-radius:10px;border-left:3px solid var(--accent)}
.part-title{font-weight:700;font-size:15px;color:var(--accent)}.part p+p{color:var(--muted);font-size:12.5px;margin-top:2px}
.findings{display:grid;grid-template-columns:1fr 1fr;gap:18px 40px}
.finding{break-inside:avoid}
.f-head{display:flex;justify-content:space-between;gap:16px;align-items:baseline}
.f-name{font-weight:600}.f-verdict{font-weight:600;color:var(--accent);white-space:nowrap}
.f-meta{color:var(--muted);font-size:12px}.f-text{margin:6px 0 8px;font-size:12.5px}
.intro{color:var(--muted);font-size:12.5px;margin:-4px 0 14px}
table.num td{font-variant-numeric:tabular-nums;white-space:nowrap}
table.num thead th:not(:first-child),table.num td{text-align:right}
table.num tbody{break-inside:avoid}
tr.cat td{color:var(--ink);font-weight:600;font-size:12px;padding-top:10px;text-align:left!important}
.dim{color:var(--muted)}
table.bench th:first-child{width:22%}

footer{margin-top:44px;padding-top:16px;border-top:1px solid var(--line2);color:var(--muted);font-size:11.5px;line-height:1.6}
footer .brand{gap:12px;align-items:flex-start}footer .logo{width:22px;height:22px;margin-top:1px;flex-shrink:0}

@media (max-width:960px){.tools{position:static;margin:16px 16px 0;justify-content:flex-end;box-shadow:none;border:0;background:transparent;padding:0}}
@media (max-width:720px){.paper{margin:12px 0 0;padding:28px 18px;border-radius:0}.cover{flex-direction:column;align-items:flex-start}.stamp{text-align:left}
.summary{grid-template-columns:1fr}.readouts{grid-template-columns:repeat(2,minmax(0,1fr));row-gap:10px}.readouts div:nth-child(odd){padding-left:0;border-left:0}
.groups{column-count:1}.findings{grid-template-columns:1fr}h1{font-size:32px}.vol{grid-template-columns:28px 1fr auto}.vol .bar{display:none}}

@page{size:A4;margin:17mm 15mm 17mm}
@page{@top-left{font:8.5pt 'Segoe UI',sans-serif;color:#8b989f}@top-right{font:8.5pt 'Segoe UI',sans-serif;color:#8b989f}
@bottom-left{font:8.5pt 'Segoe UI',sans-serif;color:#8b989f}@bottom-right{font:8.5pt 'Segoe UI',sans-serif;color:#8b989f}}
@page:first{@top-left{content:none}@top-right{content:none}}
@media print{
html{background:#fff}body{font-size:9.5pt}
.tools{display:none}
.paper{max-width:none;margin:0;padding:0;box-shadow:none;border-radius:0}
.hero{padding-top:26px}
.groups{column-gap:26px}tbody th{width:48%;padding-right:8px}.wide tbody th{width:24%}
td+td{padding-left:10px}
.readouts dd{font-size:20px}
.sheet{margin-top:28px}
.keep,.head,.plate,h3{break-inside:avoid;break-after:avoid}
.summary a:hover{background:var(--paper)}
.part{break-before:page;margin-top:0}
footer{break-inside:avoid;break-before:avoid;margin-top:28px}
}
";
}
