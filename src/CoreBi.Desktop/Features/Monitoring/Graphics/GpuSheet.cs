using CoreBi.Engine.Monitoring.Graphics;

namespace CoreBi.Desktop;

public sealed record GpuSpecRow(string Key, string Value, string? Note = null, string? Tip = null);
public sealed record GpuSpecGroup(string Title, IReadOnlyList<GpuSpecRow> Rows);

/// <summary>
/// Ficha técnica da página Placa de vídeo, montada a partir das seções do motor (GpuInfoCollector): rótulos curtos, nenhuma linha
/// sem valor e nenhum dado repetido entre grupos. Os números fixos da placa (memória, núcleos, clocks, limite) ficam só aqui.
/// </summary>
internal static class GpuSheet
{
    /// <summary>Primeiro valor útil de cada chave, em todas as seções (as linhas "—" e "Sem dados" ficam de fora).</summary>
    public static Dictionary<string, string> Flatten(GpuInfo g)
    {
        var d = new Dictionary<string, string>();
        foreach (var section in g.Sections)
            foreach (var row in section.Children)
                if (row.Value.Length > 0 && row.Value != "—" && row.Header != "Sem dados") d.TryAdd(row.Header, row.Value);
        return d;
    }

    private static string? V(Dictionary<string, string> d, string key) => d.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Frase curta sob o nome: só a memória ("8 GB GDDR6"), o dado que se confere primeiro numa placa. Chip, arquitetura e driver
    /// ficam na ficha (na faixa ao vivo, uma frase longa disputava espaço com as leituras). Vazia sem o tamanho.
    /// </summary>
    public static string Subtitle(Dictionary<string, string> d)
    {
        if (V(d, "Tamanho") is not { } t) return "";
        string size = t.Split(" (")[0];
        return V(d, "Tipo") is { } k && (k.StartsWith("GDDR") || k.StartsWith("HBM")) ? $"{size} {k}" : size;
    }

    public static List<GpuSpecGroup> Groups(GpuInfo g, Dictionary<string, string> d)
    {
        var groups = new List<GpuSpecGroup>();
        void Group(string title, params GpuSpecRow?[] rows)
        {
            var kept = rows.Where(r => r != null).Cast<GpuSpecRow>().ToList();
            if (kept.Count > 0) groups.Add(new(title, kept));
        }
        GpuSpecRow? R(string key, string? label = null, string? note = null, string? tip = null) =>
            V(d, key) is { } v ? new(label ?? key, v, note, tip) : null;

        if (V(d, "Falha ao ler") is { } error) Group("Leitura", new GpuSpecRow("Falha ao ler", error));

        // O nome da placa é o título da página (o completo na dica), então a ficha não o repete como "Modelo".
        Group("Identificação",
            R("Fabricante da placa"),
            R("Chip"),
            R("Arquitetura"),
            R("Processo"),
            R("Área do die"),
            R("Transistores"),
            R("Lançamento"),
            R("Revisão"),
            R("Device ID", "ID do dispositivo", tip: "Fabricante e modelo no barramento PCI (VEN DEV · SUBSYS)."));

        Group("Unidades de processamento",
            R("CUDA cores", tip: "Os núcleos que desenham a imagem."),
            R("Stream processors", tip: "Os núcleos que desenham a imagem."),
            R("SMs", tip: "Blocos que agrupam os núcleos."),
            R("Compute units", tip: "Blocos que agrupam os núcleos."),
            R("Tensor cores", tip: "Fazem as contas de IA, como o DLSS."),
            R("RT cores", tip: "Fazem o ray tracing."),
            R("Ray accelerators", tip: "Fazem o ray tracing."),
            R("ROPs / TMUs"),
            R("Pixel fillrate"),
            R("Texture fillrate"),
            R("FP32 (teórico)", "FP32", "teórico"));

        Group("Memória",
            R("Tamanho"),
            R("Reservada (dedicada)", "Reservada"),
            R("Tipo"),
            R("Barramento", tip: "Quantos bits a placa lê da VRAM de uma vez."),
            R("Clock da memória"),
            R("Largura de banda"),
            R("Compartilhada do sistema", "Compartilhada da RAM", tip: "Quanto da RAM o Windows deixa a placa usar quando a VRAM acaba. É bem mais lenta que a VRAM."),
            V(d, "Resizable BAR") is { } bar
                ? new GpuSpecRow("Resizable BAR", bar.StartsWith("Ativado") ? "Ativado" : bar, null, bar.StartsWith("Ativado (") ? bar[9..^1] : null)
                : null);

        Group("Clocks e energia",
            R("Base (referência)", "Clock base", "referência"),
            R("Boost (referência)", "Clock boost", "referência"),
            R("Máximo (curva V/F)", "Clock máximo", tip: "O mais alto da curva de tensão e frequência da placa."),
            R("Video engine máx.", "Clock de vídeo", "máximo"),
            R("TDP (referência)", "TDP", "referência"),
            // Limite atual igual ao padrão da placa (o caso comum, sem ajuste): uma linha só, com a nota "padrão"
            V(d, "Limite padrão da placa") == V(d, "Limite atual")
                ? R("Limite atual", "Limite de energia", "padrão", "O máximo que a placa pode puxar agora. É o padrão da placa.")
                : R("Limite atual", "Limite de energia", tip: "O máximo que a placa pode puxar agora."),
            V(d, "Limite padrão da placa") == V(d, "Limite atual") ? null : R("Limite padrão da placa", "Limite padrão"),
            R("Faixa ajustável"));

        // O driver antes do barramento: na ficha em 4 colunas cada grupo entra na coluna mais curta, e nessa ordem as colunas
        // ficam com alturas parecidas (o driver, longo, vai para baixo da memória; o barramento e as tecnologias, curtos, completam as outras).
        Group(V(d, "BIOS (VBIOS)") != null ? "Driver e BIOS" : "Driver",
            R("Versão do driver", "Versão"),
            R("Data do driver", "Data"),
            R("Assinatura digital", "Assinatura"),
            R("BIOS (VBIOS)", "VBIOS", tip: "Firmware da própria placa de vídeo."),
            R("Versão CUDA", "CUDA"),
            R("Compute capability"));

        Group("Barramento",
            R("Interface"),
            V(d, "Em uso agora") is not { } now ? null
                : new GpuSpecRow("Em uso", now.Split(" (")[0], null, "Em repouso a conexão cai para economizar energia e volta ao máximo em jogo."),
            R("Localização"));

        Group("Tecnologias",
            R("DirectX"),
            V(d, "APIs") is { } apis ? new GpuSpecRow("APIs", apis.Replace(" · ", ", ")) : null,
            V(d, "Recursos da arquitetura") is { } feats ? new GpuSpecRow("Recursos", feats.Replace(" · ", ", ")) : null);

        return groups;
    }
}
