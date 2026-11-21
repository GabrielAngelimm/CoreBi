using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreBi.Engine.Reports;

/// <summary>
/// Gera relatórios em HTML (arquivo único pronto para tela e impressão A4) ou JSON, omitindo números de série
/// e o nome do usuário local.
/// </summary>
public static class ReportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Chaves que não devem sair do computador no relatório.</summary>
    public static bool IsPrivate(string key) =>
        key.Contains("série", StringComparison.OrdinalIgnoreCase)
        || key.Contains("serial", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Usuário", StringComparison.OrdinalIgnoreCase);

    public static string Html(ReportData data) => ReportHtmlRenderer.Render(data);

    public static string Json(ReportData d) => JsonSerializer.Serialize(new
    {
        gerado_em = d.Date.ToString("s"),
        computador = d.Machine,
        versao_do_programa = d.AppVersion,
        fichas = d.Sheets,
        sistema = d.System,
        placas_de_video = d.Gpus,
        discos = d.Disks.Select(x => new { x.Name, x.Details, saude = x.Verdict, x.Reasons, metricas = SafeMetrics(x.Metrics) }),
        energia = d.Power.Select(x => new { x.Name, x.Details, estado = x.Verdict, x.Reasons, metricas = SafeMetrics(x.Metrics) }),
        rede = d.Network.Select(x => new { x.Name, x.Details, estado = x.Verdict, x.Reasons, metricas = SafeMetrics(x.Metrics) }),
        sensores = d.Sensors,
        benchmarks = d.Benchmarks,
    }, JsonOptions);

    private static Dictionary<string, string> SafeMetrics(IEnumerable<KeyValuePair<string, string>> metrics)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in metrics)
        {
            if (IsPrivate(key)) continue;
            string uniqueKey = key;
            int suffix = 2;
            while (map.ContainsKey(uniqueKey)) uniqueKey = $"{key} ({suffix++})";
            map[uniqueKey] = value;
        }
        return map;
    }
}
