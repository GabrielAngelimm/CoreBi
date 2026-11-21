using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

public static partial class SystemSheets
{
    private static Sheet GpuSheet(SystemInventory inv, IReadOnlyList<GpuInfo> gpus)
    {
        var adapters = inv.Gpus;
        if (adapters.FirstOrDefault() is not { } g) return Missing(Gpu, "Placa de vídeo", inv.Errors.GetValueOrDefault(SystemInventory.GpuTitle));
        var spec = gpus.FirstOrDefault(x => x.Name == g.Name);
        ulong vram = spec?.DedicatedVramBytes > 0 ? spec.DedicatedVramBytes : g.Vram ?? 0;
        var (vValue, vUnit) = vram > 0 ? Split(Fmt.Vram(vram)) : ("—", "");
        var primary = inv.Displays.FirstOrDefault(d => d.Primary) ?? inv.Displays.FirstOrDefault();

        // Chip, arquitetura, memória, clocks e TDP vêm da leitura da página Placa de vídeo (banco de especificações + driver).
        string? G(string key) => spec == null ? null : Find(spec.Sections, key);
        var id = new List<SpecRow> { new("Modelo", g.Name), new("Fabricante do chip", g.Vendor) };
        if (G("Fabricante da placa") is { } board) id.Add(new("Fabricante da placa", board));
        if (G("Chip") is { } chip) id.Add(new("Chip", chip));
        if (G("Arquitetura") is { } gpuArch) id.Add(new("Arquitetura", gpuArch));
        if (G("Processo") is { } process) id.Add(new("Processo", process));
        if (G("Lançamento") is { } release) id.Add(new("Lançamento", release));

        var memory = new List<SpecRow>();
        if (vram > 0) memory.Add(new("Tamanho", Fmt.Vram(vram)));
        if (G("Compartilhada do sistema") is { } shared) memory.Add(new("Compartilhada da RAM", shared, null,
            "Quanto da RAM do PC o Windows deixa a placa usar quando a VRAM acaba. É bem mais lenta que a VRAM."));
        if (G("Tipo") is { } memType) memory.Add(new("Tipo", memType));
        if (G("Barramento") is { } bus) memory.Add(new("Barramento", bus, null, "Quantos bits a placa lê da VRAM de uma vez."));
        if (G("Largura de banda") is { } bandwidth) memory.Add(new("Largura de banda", bandwidth));
        if (G("Resizable BAR") is { } bar) memory.Add(new("Resizable BAR", bar.StartsWith("Ativado") ? "Ativado" : bar, null,
            "Deixa o processador acessar toda a VRAM de uma vez. Alguns jogos ganham desempenho."));

        var units = new List<SpecRow>();
        foreach (var (key, tip) in new[]
        {
            ("CUDA cores", "Os núcleos que desenham a imagem. Mais núcleos, mais desempenho dentro da mesma geração."),
            ("Stream processors", "Os núcleos que desenham a imagem. Mais núcleos, mais desempenho dentro da mesma geração."),
            ("SMs", "Blocos que agrupam os núcleos."), ("Compute units", "Blocos que agrupam os núcleos."),
            ("RT cores", "Fazem o ray tracing (luz e reflexos realistas)."), ("Ray accelerators", "Fazem o ray tracing (luz e reflexos realistas)."),
            ("Tensor cores", "Fazem as contas de IA, como o DLSS."),
        })
            if (G(key) is { } v) units.Add(new(key, v, null, tip));

        var clocks = new List<SpecRow>();
        if (G("Base (referência)") is { } baseClock) clocks.Add(new("Clock base", baseClock, "referência"));
        if (G("Boost (referência)") is { } boost) clocks.Add(new("Clock boost", boost, "referência"));
        if (G("TDP (referência)") is { } tdp) clocks.Add(new("TDP", tdp, null, "Consumo de referência da fabricante do chip."));
        if (G("Limite atual") is { } limit) clocks.Add(new("Limite de energia", limit, null, "O máximo que a placa pode puxar agora, definido pela fabricante da placa."));
        if (G("Interface") is { } pcie)
        {
            string? now = G("Em uso agora")?.Split(" (")[0];
            clocks.Add(new("Interface", pcie, now != null && now != pcie ? $"agora em {now}" : null,
                "Conexão entre a placa e a placa-mãe. Em repouso ela cai para economizar energia e volta ao máximo em jogo."));
        }

        var driver = new List<SpecRow> { new("Versão", g.DriverVersion), new("Data", Fmt.Date(g.DriverDate), Fmt.Ago(g.DriverDate)) };
        if (g.WindowsDriverVersion != g.DriverVersion)
            driver.Add(new("Versão no Windows", g.WindowsDriverVersion, null, $"O mesmo driver {g.DriverVersion}, no formato que o Windows usa."));
        if (G("BIOS (VBIOS)") is { } vbios) driver.Add(new("BIOS da placa", vbios, null, "Firmware da própria placa de vídeo (VBIOS)."));
        if (G("DirectX") is { } dx) driver.Add(new("DirectX", dx));

        var groups = new List<SpecGroup> { new("Identificação", id) };
        if (memory.Count > 0) groups.Add(new("Memória (VRAM)", memory, Help: "Memória própria da placa: guarda texturas e quadros. Jogos em resolução alta pedem mais."));
        // As telas entram logo depois da memória: cada grupo vai para a coluna mais curta, e assim as três fecham parelhas.
        foreach (var d in inv.Displays)
        {
            var hz = d.BelowMax
                ? Verdict("Atualização", $"{d.Hz} Hz", Tone.Warn, $"aceita {d.MaxHz} Hz",
                    "Ajuste em Configurações › Sistema › Tela › Tela avançada.")
                : d.MaxHz > 0 && d.Hz >= d.MaxHz - 1
                    ? Verdict("Atualização", $"{d.Hz} Hz", Tone.Ok, "máximo da tela")
                    : new SpecRow("Atualização", $"{d.Hz} Hz");
            var rows = new List<SpecRow>
            {
                new("Monitor", d.Monitor),
                hz,
            };
            if (d.Inches is double inch) rows.Insert(1, new("Tamanho", $"{inch:0}\""));
            if (d.Connection != null) rows.Add(new("Conexão", d.Connection, null, "O cabo entre a placa de vídeo e a tela."));
            rows.Insert(1, new("Resolução", $"{d.Width} × {d.Height}"));
            // O monitor informa a semana; o primeiro dia dela dá o mês (semana 22 de 2023 → maio de 2023).
            if (d.Year is int year)
                rows.Add(new("Fabricado em", d.Week is int week ? Cap(new DateTime(year, 1, 1).AddDays((week - 1) * 7).ToString("MMMM 'de' yyyy", Br)) : year.ToString(Br),
                    null, d.Week is int w ? $"O monitor informa a semana {w} de {year}." : null));
            if (inv.Displays.Count > 1) rows.Add(new("Posição", d.Primary ? "Tela principal" : "Estendida", $"{d.X}, {d.Y}"));
            if (adapters.Count > 1) rows.Add(new("Ligada em", d.Adapter));
            groups.Add(new(inv.Displays.Count > 1 ? $"Tela {d.Number}" + (d.Primary ? " (principal)" : "") : "Tela", rows,
                Help: d == inv.Displays[0] ? "Atualização é quantas imagens por segundo a tela mostra. Acima de 60 Hz o movimento fica mais fluido." : null));
        }
        if (units.Count > 0) groups.Add(new("Unidades de processamento", units));
        if (clocks.Count > 0) groups.Add(new("Clocks e energia", clocks));
        groups.Add(new("Driver", driver));
        if (adapters.Count > 1)
            groups.Add(new("Outras placas de vídeo", adapters.Skip(1).Select(a =>
                new SpecRow(a.Name, string.Join(" · ", new[] { a.Vram is ulong v && v > 0 ? Fmt.Vram(v) : null, $"driver {a.DriverVersion}" }.Where(x => x != null)))).ToList()));

        return new Sheet
        {
            Key = Gpu,
            Eyebrow = g.Vendor,
            Title = WithoutVendor(g.Name),
            Subtitle = g.Name,
            Readouts = new()
            {
                new("Memória", vValue, vUnit),
                new("Driver", g.DriverVersion),
                new("Resolução", primary != null ? $"{primary.Width} × {primary.Height}" : "—", "", inv.Displays.Count > 1 ? "Da tela principal" : null),
                new("Atualização", primary != null ? primary.Hz.ToString(Br) : "—", primary != null ? "Hz" : ""),
            },
            Groups = groups,
            Link = new("Clocks, temperatura e consumo ao vivo em Placa de vídeo", "PageGpu"),
        };
    }

    /// <summary>Tira só o fabricante ("NVIDIA GeForce RTX 4060 Ti" → "GeForce RTX 4060 Ti"); a família continua no título.</summary>
    private static string WithoutVendor(string name)
    {
        foreach (var p in new[] { "NVIDIA ", "AMD ", "Intel(R) ", "Intel " })
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return name[p.Length..].Replace("(TM)", "").Replace("(R)", "").Trim();
        return name;
    }

    /// <summary>Primeiro valor útil com essa chave nas seções da página Placa de vídeo.</summary>
    private static string? Find(IEnumerable<InfoNode> nodes, string key)
    {
        foreach (var n in nodes)
        {
            if (n.Header == key && n.Value.Length > 0 && n.Value != "—") return n.Value;
            if (Find(n.Children, key) is { } v) return v;
        }
        return null;
    }
}
