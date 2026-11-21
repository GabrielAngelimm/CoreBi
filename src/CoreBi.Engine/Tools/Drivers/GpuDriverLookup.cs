using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Drivers;

/// <summary>Consulta de versão mais recente e link oficial de download de drivers de GPU (NVIDIA e AMD).</summary>
public static partial class GpuDriverLookup
{
    private static readonly HttpClient Http = CreateClient();

    [GeneratedRegex(@"RX\s*(\d{4})\s*(XTX|XT|GRE|M)?", RegexOptions.IgnoreCase)]
    private static partial Regex AmdModelRegex();

    [GeneratedRegex(@"https://drivers\.amd\.com/drivers/[^""'\s<>\\]*win11[^""'\s<>\\]*\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex AmdWin11ExeRegex();

    [GeneratedRegex(@"https://drivers\.amd\.com/drivers/[^""'\s<>\\]*\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex AmdAnyExeRegex();

    [GeneratedRegex(@"edition-([\d.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AmdEditionRegex();

    [GeneratedRegex(@"(RTX|GTX|GT)\s*(\d{3,4})", RegexOptions.IgnoreCase)]
    private static partial Regex NvidiaModelRegex();

    [GeneratedRegex(@"^NVIDIA\s+", RegexOptions.IgnoreCase)]
    private static partial Regex NvidiaPrefixRegex();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CoreBi");
        return c;
    }

    public static async Task CheckNvidiaAsync(GpuDriverStatus status, bool studio, CancellationToken ct)
    {
        status.Latest = ""; status.DownloadUrl = ""; status.SizeText = "";
        try
        {
            var (psid, pfid) = await ResolveNvidiaIdsAsync(status.GpuName, ct).ConfigureAwait(false);
            string url = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" +
                         $"?func=DriverManualLookup&psid={psid}&pfid={pfid}&osID=57&languageCode=1033&beta=0&isWHQL=1&dch=1" +
                         $"&upCRD={(studio ? 1 : 0)}&qnf=0&sort1=1&numberOfResults=1";
            string json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            string? ver = null, dl = null, size = null;
            FindFields(doc.RootElement, ref ver, ref dl, ref size);
            if (string.IsNullOrEmpty(ver) || string.IsNullOrEmpty(dl))
            {
                status.Message = "A NVIDIA não devolveu um driver para esta placa. Use a página oficial.";
                return;
            }
            status.Latest = ver.Trim();
            status.DownloadUrl = dl.Replace("http://", "https://");
            status.SizeText = size ?? "";
            status.Message = status.UpdateAvailable ? "Há um driver mais novo." : "Você já está com o mais recente.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { status.Message = "Não foi possível consultar a NVIDIA: " + ex.Message; }
    }

    /// <summary>
    /// AMD não tem API de versão: consulta a página oficial do modelo para localizar o instalador; se mudar ou bloquear,
    /// deixa a URL oficial pronta para abrir manualmente.
    /// </summary>
    public static async Task CheckAmdAsync(GpuDriverStatus status, CancellationToken ct)
    {
        status.Latest = ""; status.DownloadUrl = ""; status.SizeText = "";
        try
        {
            var m = AmdModelRegex().Match(status.GpuName);
            if (!m.Success) { status.Message = "Modelo não reconhecido para consulta automática. Use a página oficial."; return; }
            int n = int.Parse(m.Groups[1].Value);
            string fam = $"radeon-rx-{n / 1000}000-series";
            string suffix = m.Groups[2].Success ? "-" + m.Groups[2].Value.ToLowerInvariant() : "";
            string page = $"https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/{fam}/amd-radeon-rx-{n}{suffix}.html";
            status.OfficialPage = page;
            using var req = new HttpRequestMessage(HttpMethod.Get, page);
            req.Headers.Referrer = new Uri("https://www.amd.com/");
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var hit = AmdWin11ExeRegex().Match(html);
            if (!hit.Success) hit = AmdAnyExeRegex().Match(html);
            if (!hit.Success) { status.Message = "O site da AMD não devolveu o link do instalador. Abra a página oficial do seu modelo."; return; }
            status.DownloadUrl = hit.Value;
            var v = AmdEditionRegex().Match(hit.Value);
            status.Latest = v.Success ? v.Groups[1].Value : Path.GetFileNameWithoutExtension(hit.Value);
            status.Message = "O Windows mostra o número interno do driver, que não dá para comparar com a versão do pacote da AMD.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { status.Message = "Não foi possível consultar a AMD: " + ex.Message; }
    }

    private static void FindFields(JsonElement e, ref string? ver, ref string? dl, ref string? size)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    if (p.NameEquals("Version") && ver == null) ver = p.Value.GetString();
                    else if (p.NameEquals("DownloadURL") && dl == null) dl = p.Value.GetString();
                    else if (p.NameEquals("DownloadURLFileSize") && size == null) size = p.Value.GetString();
                }
                else FindFields(p.Value, ref ver, ref dl, ref size);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in e.EnumerateArray()) FindFields(i, ref ver, ref dl, ref size);
        }
    }

    private static async Task<(int Psid, int Pfid)> ResolveNvidiaIdsAsync(string gpuName, CancellationToken ct)
    {
        bool mobile = HardwareNaming.IsMobileGpu(gpuName);
        var m = NvidiaModelRegex().Match(gpuName);
        if (!m.Success) throw new InvalidOperationException("Modelo de placa não reconhecido: " + gpuName);
        string prefix = m.Groups[1].Value.ToUpperInvariant();
        int n = int.Parse(m.Groups[2].Value);

        string series = prefix == "RTX"
            ? $"RTX {n / 100} Series"
            : n switch
            {
                >= 1600 and < 1700 => "16 Series",
                >= 1000 and < 1100 => "10 Series",
                >= 900 and < 1000 => "900 Series",
                >= 700 and < 800 => "700 Series",
                _ => $"{n / 100 * 100} Series",
            };

        string xml = await Http.GetStringAsync("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2&ParentID=1", ct).ConfigureAwait(false);
        var seriesList = XDocument.Parse(xml).Descendants("LookupValue")
            .Select(x => (Name: (string?)x.Element("Name") ?? "", Value: (string?)x.Element("Value") ?? "")).ToList();
        var cand = seriesList.Where(s => s.Name.Contains(series, StringComparison.OrdinalIgnoreCase)
                                         && s.Name.Contains("Notebook", StringComparison.OrdinalIgnoreCase) == mobile)
                             .OrderBy(s => s.Name.Length).ToList();
        if (cand.Count == 0) throw new InvalidOperationException("Série da placa não encontrada na NVIDIA (" + series + ").");
        int psid = int.Parse(cand[0].Value);

        xml = await Http.GetStringAsync($"https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3&ParentID={psid}", ct).ConfigureAwait(false);
        var prods = XDocument.Parse(xml).Descendants("LookupValue")
            .Select(x => (Name: (string?)x.Element("Name") ?? "", Value: (string?)x.Element("Value") ?? "")).ToList();
        if (prods.Count == 0) throw new InvalidOperationException("Nenhum produto listado para a série.");
        string key = NvidiaPrefixRegex().Replace(gpuName, "").Replace("Laptop GPU", "").Trim();
        var exact = prods.FirstOrDefault(p => string.Equals(p.Name.Trim(), key, StringComparison.OrdinalIgnoreCase));
        var pick = exact.Value != null ? exact : prods.FirstOrDefault(p => p.Name.Contains(m.Value, StringComparison.OrdinalIgnoreCase));
        if (pick.Value == null) pick = prods[0]; // o driver é unificado por série
        return (psid, int.Parse(pick.Value));
    }
}
