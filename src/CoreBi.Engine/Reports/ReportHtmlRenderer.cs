using System.Globalization;
using System.Net;
using System.Text;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Reports;

internal static class ReportHtmlRenderer
{
    private const string IconMoon = "<svg class=\"i-moon\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z\"/></svg>";
    private const string IconSun = "<svg class=\"i-sun\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4\"/></svg>";
    private const string IconPrint = "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M7 9V3h10v6M7 17H5a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2\"/><path d=\"M7 14h10v7H7z\"/></svg>";

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string CssText(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Channel(string? c) => c is "cpu" or "gpu" or "memory" or "storage" or "board" or "net" ? c : "steel";

    public static string Render(ReportData d)
    {
        var sb = new StringBuilder(96 * 1024);
        string date = d.Date.ToString("dd/MM/yyyy 'às' HH:mm", ByteFormat.PtBr);
        string version = d.AppVersion.Length > 0 ? $"CoreBi {d.AppVersion}" : "CoreBi";

        sb.Append($"<!doctype html><html lang=\"pt-BR\" data-theme=\"{(d.Dark ? "dark" : "light")}\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"color-scheme\" content=\"light dark\">");
        sb.Append($"<title>{E(d.Machine)} · Relatório do sistema</title><style>{ReportAssets.Css}");
        // Cabeçalho e rodapé de cada folha impressa (navegadores Chromium; os outros só ignoram).
        sb.Append($"@page{{@top-left{{content:\"CoreBi · Relatório do sistema\"}}@top-right{{content:\"{CssText(d.Machine)}\"}}");
        sb.Append($"@bottom-left{{content:\"Gerado em {CssText(date)}\"}}@bottom-right{{content:\"Página \" counter(page) \" de \" counter(pages)}}}}");
        sb.Append("</style></head><body>");

        sb.Append("<div class=\"tools\" role=\"toolbar\" aria-label=\"Relatório\">");
        sb.Append($"<button type=\"button\" id=\"theme\" aria-pressed=\"false\">{IconMoon}{IconSun}<span>Tema escuro</span></button>");
        sb.Append($"<button type=\"button\" id=\"print\" class=\"primary\">{IconPrint}<span>Imprimir ou salvar PDF</span></button></div>");

        sb.Append("<main class=\"paper\">");
        sb.Append("<header class=\"cover\"><div class=\"brand\">");
        if (!string.IsNullOrEmpty(d.LogoBase64)) sb.Append($"<img class=\"logo on-light\" alt=\"\" src=\"data:image/png;base64,{d.LogoBase64}\">");
        if (!string.IsNullOrEmpty(d.LogoOnDarkBase64)) sb.Append($"<img class=\"logo on-dark\" alt=\"\" src=\"data:image/png;base64,{d.LogoOnDarkBase64}\">");
        sb.Append("<div><p class=\"brand-name\">CoreBi</p><p class=\"brand-doc\">Relatório do sistema</p></div></div>");
        sb.Append($"<dl class=\"stamp\"><div><dt>Gerado em</dt><dd>{E(date)}</dd></div><div><dt>Versão</dt><dd>{E(version)}</dd></div></dl></header>");
        sb.Append($"<div class=\"hero\"><h1>{E(d.Machine)}</h1>");
        var headline = d.Sheets.Where(s => s.Category is "Sistema operacional" or "Processador" or "Placa de vídeo" or "Memória RAM")
            .Select(s => s.Title).Where(t => t.Length > 0 && t != "—");
        sb.Append($"<p class=\"lead\">{string.Join("<i>·</i>", headline.Select(E))}</p></div>");

        if (d.Sheets.Count > 0)
        {
            sb.Append("<nav class=\"summary\" aria-label=\"Sumário\">");
            for (int i = 0; i < d.Sheets.Count; i++)
            {
                var s = d.Sheets[i];
                sb.Append($"<a href=\"#s{i + 1}\" class=\"c-{Channel(s.Channel)}\"><span class=\"n\">{i + 1}</span><span class=\"cat\">{E(s.Category)}</span>");
                sb.Append($"<span class=\"name\">{E(s.Title)}</span><span class=\"spec\">{E(s.Summary)}</span></a>");
            }
            sb.Append("</nav>");
        }

        for (int i = 0; i < d.Sheets.Count; i++) RenderSheet(sb, d.Sheets[i], i + 1);

        int n = d.Sheets.Count;
        bool any = d.Disks.Count + d.Power.Count + d.Network.Count + d.Sensors.Count + d.Benchmarks.Count > 0;
        if (any) sb.Append("<div class=\"part\"><p class=\"part-title\">Diagnóstico</p><p>Estado lido no momento da exportação, nas páginas Discos, Energia, Rede, Sensores e Benchmarks.</p></div>");
        if (d.Disks.Count > 0) RenderFindings(sb, ++n, "Saúde dos discos", "storage", d.Disks);
        if (d.Power.Count > 0) RenderFindings(sb, ++n, "Bateria e energia", "steel", d.Power);
        if (d.Network.Count > 0) RenderFindings(sb, ++n, "Teste de rede", "net", d.Network);
        if (d.Sensors.Count > 0) RenderSensors(sb, ++n, d.Sensors);
        if (d.Benchmarks.Count > 0) RenderBenchmarks(sb, ++n, d.Benchmarks);

        sb.Append("<footer><div class=\"brand small\">");
        if (!string.IsNullOrEmpty(d.LogoBase64)) sb.Append($"<img class=\"logo on-light\" alt=\"\" src=\"data:image/png;base64,{d.LogoBase64}\">");
        if (!string.IsNullOrEmpty(d.LogoOnDarkBase64)) sb.Append($"<img class=\"logo on-dark\" alt=\"\" src=\"data:image/png;base64,{d.LogoOnDarkBase64}\">");
        sb.Append($"<p>Gerado pelo {E(version)} em {E(date)}. Os dados vêm do Windows, do driver da placa de vídeo e dos sensores da placa-mãe. ");
        sb.Append("Números de série e o nome do usuário ficam fora deste relatório.</p></div></footer>");
        sb.Append($"</main><script>{ReportAssets.Script}</script></body></html>");
        return sb.ToString();
    }

    private static void RenderHead(StringBuilder sb, int number, string title) =>
        sb.Append($"<div class=\"head\"><span class=\"n\">{number}</span><h2>{E(title)}</h2></div>");

    private static void RenderSheet(StringBuilder sb, ReportSheet s, int number)
    {
        sb.Append($"<section class=\"sheet c-{Channel(s.Channel)}\" id=\"s{number}\"><div class=\"keep\">");
        RenderHead(sb, number, s.Category);
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

        var flow = s.Groups.Where(g => g.Columns == null).ToList();
        if (flow.Count > 0)
        {
            sb.Append("<div class=\"groups\">");
            foreach (var g in flow) RenderGroup(sb, g);
            sb.Append("</div>");
        }
        foreach (var g in s.Groups.Where(g => g.Columns != null)) RenderGroup(sb, g);
        sb.Append("</section>");
    }

    private static void RenderGroup(StringBuilder sb, ReportGroup g)
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

    private static void RenderFindings(StringBuilder sb, int number, string title, string channel, List<ReportFinding> items)
    {
        sb.Append($"<section class=\"sheet c-{Channel(channel)}\">");
        RenderHead(sb, number, title);
        sb.Append("<div class=\"findings\">");
        foreach (var x in items)
        {
            string tone = x.Tone is "ok" or "warn" or "danger" ? x.Tone : "none";
            sb.Append($"<div class=\"finding\"><div class=\"f-head\"><p class=\"f-name\">{E(x.Name)}</p><p class=\"f-verdict t-{tone}\"><i></i>{E(HardwareNaming.Sentence(x.Verdict))}</p></div>");
            if (x.Details.Length > 0) sb.Append($"<p class=\"f-meta\">{E(x.Details)}</p>");
            if (x.Reasons.Length > 0) sb.Append($"<p class=\"f-text\">{E(x.Reasons)}</p>");
            if (x.Metrics.Count > 0)
            {
                sb.Append("<table><tbody>");
                foreach (var m in x.Metrics.Where(m => !ReportBuilder.IsPrivate(m.Key)))
                    sb.Append($"<tr><th scope=\"row\">{E(m.Key)}</th><td>{E(m.Value)}</td></tr>");
                sb.Append("</tbody></table>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div></section>");
    }

    private static void RenderSensors(StringBuilder sb, int number, List<ReportSensor> sensors)
    {
        sb.Append("<section class=\"sheet c-steel\">");
        RenderHead(sb, number, "Sensores");
        sb.Append("<p class=\"intro\">Leitura no momento da exportação. Mínimo e máximo contam desde que o CoreBi abriu ou desde a última vez que as estatísticas foram zeradas.</p>");
        foreach (var hw in sensors.GroupBy(s => s.Hardware))
        {
            sb.Append($"<div class=\"group wide\"><h3>{E(hw.Key)}</h3><table class=\"num\"><thead><tr><th></th><th>Atual</th><th>Mín.</th><th>Máx.</th></tr></thead>");
            foreach (var cat in hw.GroupBy(s => s.Category))
            {
                sb.Append($"<tbody><tr class=\"cat\"><td colspan=\"4\">{E(HardwareNaming.Sentence(cat.Key))}</td></tr>");
                foreach (var s in cat)
                    sb.Append($"<tr><th scope=\"row\">{E(s.Name)}</th><td>{E(s.Value)}</td><td class=\"dim\">{E(s.Min)}</td><td class=\"dim\">{E(s.Max)}</td></tr>");
                sb.Append("</tbody>");
            }
            sb.Append("</table></div>");
        }
        sb.Append("</section>");
    }

    private static void RenderBenchmarks(StringBuilder sb, int number, List<ReportBench> list)
    {
        sb.Append("<section class=\"sheet c-steel\">");
        RenderHead(sb, number, "Benchmarks e testes");
        sb.Append("<div class=\"group wide\"><table class=\"bench\"><thead><tr><th>Data</th><th>Teste</th><th>Resultado</th></tr></thead><tbody>");
        foreach (var b in list)
            sb.Append($"<tr><td class=\"dim\">{b.Date.ToString("dd/MM/yyyy HH:mm", ByteFormat.PtBr)}</td><td>{E(b.Test)}</td><td>{E(b.Summary)}</td></tr>");
        sb.Append("</tbody></table></div></section>");
    }
}
