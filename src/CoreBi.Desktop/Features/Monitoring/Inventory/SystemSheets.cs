using System.Globalization;
using CoreBi.Engine.Games;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Storage;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

// ---------------- Modelos da ficha ----------------

/// <summary>Botão do índice: ícone e categoria. Nome e especificação do componente aparecem na dica e na ficha.</summary>
public sealed record IndexItem(string Key, string Glyph, string Category, string Name, string Spec)
{
    public string Tip => Spec.Length > 0 ? $"{Name} · {Spec}" : Name;

    /// <summary>Canal do componente, o mesmo dos gráficos: pinta o ladrilho do botão e a luz da placa (Steel = aço, neutro).</summary>
    public string Channel => Key switch
    {
        SystemSheets.Cpu => "Cpu",
        SystemSheets.Gpu => "Gpu",
        SystemSheets.Ram => "Memory",
        SystemSheets.Storage => "Storage",
        SystemSheets.Board => "Board",
        _ => "Steel",
    };
}

/// <summary>Leitura grande da placa. First tira o fio e o recuo da primeira.</summary>
public sealed record Readout(string Label, string Value, string Unit = "", string? Tip = null)
{
    public bool First { get; init; }
}

/// <summary>
/// Linha chave/valor. Note aparece em cinza depois do valor ("há 1 ano"); Tone acende um LED antes dele (verde: está
/// certo, âmbar: vale ajustar); Help é uma linha de explicação embaixo, para o que pede contexto. Clicar copia o valor.
/// </summary>
public sealed record SpecRow(string Key, string Value, string? Note = null, string? Tip = null, Tone Tone = Tone.None, string? Help = null)
{
    /// <summary>Uma célula por coluna do grupo (módulos de memória lado a lado); Value guarda o texto para copiar.</summary>
    public List<string>? Cells { get; init; }
}

/// <summary>Grupo de linhas com uma frase que explica o assunto. Tokens: lista em linha (instruções do processador).</summary>
public sealed record SpecGroup(string Title, List<SpecRow> Rows, List<SpecToken>? Tokens = null, string? Help = null)
{
    /// <summary>Cabeçalho das colunas quando as linhas têm Cells.</summary>
    public List<string>? Columns { get; init; }

    /// <summary>Coluna fixa no layout de três colunas; -1 deixa o grupo ir para a mais curta.</summary>
    public int Column { get; init; } = -1;
}

public sealed record SpecToken(string Text, bool On);

public sealed record LinkItem(string Text, string Page);

public sealed class Sheet
{
    public string Key { get; init; } = "";
    public string Eyebrow { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public List<Readout> Readouts { get; init; } = new();
    public List<SpecGroup> Groups { get; init; } = new();
    public LinkItem? Link { get; init; }
    public List<DiskView>? Disks { get; init; }
}

// Armazenamento: um disco por linha (DataTemplate em SystemView.xaml)

public sealed record VolumeView(string Letter, string Name, string Line, double Weight, double Used, string Tip);
public sealed record DiskView(string Title, string Meta, string Size, string Unit, string Free, List<VolumeView> Volumes,
    string Health = "", Tone Tone = Tone.None)
{
    public bool First { get; init; }
}

/// <summary>Monta as seis fichas da página Sistema a partir do inventário (e das placas de vídeo e unidades já lidas).</summary>
public static class SystemSheets
{
    public const string Cpu = "cpu", Gpu = "gpu", Ram = "ram", Board = "board", Storage = "storage", Os = "os";

    /// <summary>Nome da categoria (como aparece na Visão geral e no relatório) → chave da ficha.</summary>
    public static string? KeyOf(string category) => category switch
    {
        SystemInventory.CpuTitle => Cpu,
        SystemInventory.GpuTitle => Gpu,
        SystemInventory.RamTitle => Ram,
        SystemInventory.BoardTitle or "Placa-mãe" or "BIOS" => Board,
        SystemInventory.StorageTitle => Storage,
        SystemInventory.OsTitle => Os,
        _ => null
    };

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private static string N(double v, string f = "0") => v.ToString(f, Br);

    public static (List<IndexItem> Index, Dictionary<string, Sheet> Sheets) Build(
        SystemInventory inv, IReadOnlyList<GpuInfo> gpus, IReadOnlyList<DriveCard> drives, DateTime now, MemoryInfo.Snapshot? memory = null)
    {
        var sheets = new Dictionary<string, Sheet>
        {
            [Cpu] = CpuSheet(inv),
            [Gpu] = GpuSheet(inv, gpus),
            [Ram] = RamSheet(inv, memory, now),
            [Board] = BoardSheet(inv, now),
            [Storage] = StorageSheet(inv, drives),
            [Os] = OsSheet(inv, now),
        };
        // Campo que o Windows não preenche não vira linha com travessão: some, e o grupo que ficar vazio também.
        foreach (var sheet in sheets.Values)
        {
            foreach (var g in sheet.Groups) g.Rows.RemoveAll(r => r.Cells == null && r.Value is "—" or "");
            sheet.Groups.RemoveAll(g => g.Rows.Count == 0 && g.Tokens is not { Count: > 0 });
        }
        var index = new List<IndexItem>();
        void Add(string key, string glyph, string category, string? error)
        {
            var s = sheets[key];
            string spec = error != null ? "Não foi possível ler" : IndexSpec(key, s, inv);
            index.Add(new IndexItem(key, glyph, category, error != null ? category : IndexName(key, s), spec));
        }
        Add(Cpu, "\uE950", "Processador", inv.Errors.GetValueOrDefault(SystemInventory.CpuTitle));
        Add(Gpu, "\uE7F4", "Placa de vídeo", inv.Errors.GetValueOrDefault(SystemInventory.GpuTitle));
        Add(Ram, "\uEEA0", "Memória RAM", inv.Errors.GetValueOrDefault(SystemInventory.RamTitle));
        Add(Board, "\uE964", "Placa-mãe e BIOS", inv.Errors.GetValueOrDefault(SystemInventory.BoardTitle));
        Add(Storage, "\uEDA2", "Armazenamento", inv.Errors.GetValueOrDefault(SystemInventory.StorageTitle));
        Add(Os, "\uE7F8", "Sistema operacional", inv.Errors.GetValueOrDefault(SystemInventory.OsTitle));
        return (index, sheets);
    }

    private static string IndexName(string key, Sheet s) => key switch
    {
        Storage => s.Disks is { Count: > 0 } ? $"{s.Title} em {s.Disks.Count} {(s.Disks.Count == 1 ? "disco" : "discos")}" : s.Title,
        _ => s.Title
    };

    /// <summary>Linha curta do índice: as leituras da ficha que identificam o componente.</summary>
    private static string IndexSpec(string key, Sheet s, SystemInventory inv)
    {
        string R(int i) => i < s.Readouts.Count ? (s.Readouts[i].Value + (s.Readouts[i].Unit.Length > 0 ? " " + s.Readouts[i].Unit : "")).Trim() : "";
        return key switch
        {
            Cpu => string.Join(" · ", new[] { $"{R(0)} núcleos", $"{R(1)} threads", R(2) }.Where(x => !x.StartsWith("—"))),
            Gpu => R(0).StartsWith("—") ? $"driver {R(1)}" : $"{R(0)} · driver {R(1)}",
            Ram => string.Join(" · ", new[] { R(0), inv.Ram?.Channels == 2 ? "dual channel" : null }.Where(x => !string.IsNullOrEmpty(x) && x != "—")),
            Board => string.Join(" · ", new[] { s.Eyebrow, $"BIOS {R(0)}" }.Where(x => x.Length > 0 && x != "—")),
            Storage => s.Readouts.Count > 1 ? $"{R(1)} livres" : "",
            Os => $"{R(0)} · build {R(1)}",
            _ => s.Subtitle
        };
    }

    /// <summary>Linha de veredito: o valor ganha o LED do tom; o texto do veredito vira a explicação embaixo.</summary>
    private static SpecRow Verdict(string key, string value, Tone tone, string? note = null, string? help = null, string? tip = null) =>
        new(key, value, note, tip, tone, help);

    // ---------------- Processador ----------------

    private static Sheet CpuSheet(SystemInventory inv)
    {
        if (inv.Cpus.FirstOrDefault() is not { } c) return Missing(Cpu, "Processador", inv.Errors.GetValueOrDefault(SystemInventory.CpuTitle));
        var t = c.Topology;
        int cores = t.Cores.Count > 0 ? t.Cores.Count : c.Cores, threads = t.Threads > 0 ? t.Threads : c.Threads;
        if (inv.Cpus.Count > 1) { cores = inv.Cpus.Sum(x => x.Cores); threads = inv.Cpus.Sum(x => x.Threads); }
        var l3 = t.Caches.Where(x => x.Level == 3).ToList();
        var (l3Value, l3Unit) = l3.Count > 0 ? Split(Fmt.Cache(l3.Sum(x => x.Size * x.Instances))) : c.L3Kb > 0 ? Split(Fmt.Cache(c.L3Kb * 1024)) : ("—", "");
        bool smt = threads > cores;
        string amd = c.Vendor == "AMD" ? "SVM" : "VT-x";

        // A placa resume; a identificação repete o nome completo, como na etiqueta do processador.
        var id = new List<SpecRow>
        {
            new("Fabricante", c.Vendor),
            new("Nome completo", c.Name),
        };
        if (c.Family != null) id.Add(new("Família · modelo · stepping", $"{c.Family} · {c.Model} · {c.Stepping}", null,
            $"Em hexadecimal: família {c.Family:X}h, modelo {c.Model:X}h. É o que identifica a geração exata do chip."));
        id.Add(new("Arquitetura", c.Architecture, c.Architecture switch { "x64" => "64 bits", "ARM64" => "ARM, 64 bits", "x86" => "32 bits", _ => null }));
        id.Add(new("Soquete", c.Socket, Help: "O encaixe na placa-mãe. Define quais processadores ela aceita numa troca."));
        if (c.Microcode != null) id.Add(new("Microcódigo", c.Microcode, null, "Revisão das correções internas do processador, carregada pela BIOS ou pelo Windows."));
        if (inv.Cpus.Count > 1) id.Add(new("Processadores", inv.Cpus.Count.ToString(Br)));

        var clocks = new List<SpecRow> { new("Núcleos", cores.ToString(Br), t.Hybrid ? $"{t.PerformanceCores} P + {t.EfficientCores} E" : null) };
        if (t.Hybrid) clocks.Add(new("Desempenho + eficiência", $"{t.PerformanceCores} + {t.EfficientCores}", null, null, Tone.None,
            "Os de desempenho (P) tocam jogos e tarefas pesadas; os de eficiência (E) cuidam do que roda em segundo plano."));
        clocks.Add(new("Threads", threads.ToString(Br), smt ? $"{(double)threads / cores:0.#} por núcleo" : "1 por núcleo"));
        clocks.Add(new(c.Vendor == "AMD" ? "SMT" : "Hyper-Threading", smt ? "Ativado" : "Desligado", null, "Cada núcleo toca duas threads ao mesmo tempo."));
        clocks.Add(new("Clock base", Fmt.Ghz(c.BaseMhz), Help: "A frequência garantida. Em uso ele sobe acima disso (boost) conforme temperatura e consumo."));
        if (c.RefClockMhz > 0)
            clocks.Add(new("Clock de referência", $"{c.RefClockMhz} MHz", c.BaseMhz > 0 ? $"× {N((double)c.BaseMhz / c.RefClockMhz, "0.#")} no clock base" : null,
                "O clock base é este valor vezes o multiplicador do processador."));

        var cache = Fmt.CacheLines(c).Select(kv => new SpecRow(kv.Key, kv.Value, kv.Key == "L3" && l3.Count == 1 ? "compartilhado" : null)).ToList();

        var features = new List<SpecRow>();
        if (c.Hypervisor)
        {
            features.Add(Verdict($"Virtualização ({amd})", "Ativada", Tone.Ok, null, "Máquinas virtuais, WSL 2 e o Subsistema para Android funcionam."));
            features.Add(new("Hyper-V / VBS", "Em uso", null, null, Tone.None, "O próprio Windows usa a virtualização para isolar partes sensíveis do sistema."));
        }
        else if (c.Virtualization is bool on)
            features.Add(on
                ? Verdict($"Virtualização ({amd})", "Ativada", Tone.Ok, null, "Máquinas virtuais, WSL 2 e o Subsistema para Android funcionam.")
                : Verdict($"Virtualização ({amd})", "Desligada", Tone.Warn, null, $"Para usar máquinas virtuais ou WSL 2, ative {amd} na BIOS."));

        var groups = new List<SpecGroup>
        {
            new("Núcleos e threads", clocks, Help: "Núcleos são processadores completos dentro do chip; threads são as tarefas que eles tocam ao mesmo tempo."),
            new("Identificação", id),
        };
        if (cache.Count > 0) groups.Add(new("Cache", cache, Help: "Memória ultrarrápida dentro do processador. L1 e L2 são de cada núcleo; o L3 é dividido entre todos. Quanto mais cache, menos ele espera pela RAM."));
        if (features.Count > 0) groups.Add(new("Recursos", features));
        if (c.Instructions.Count > 0)
            groups.Add(new("Instruções", new(), c.Instructions.Select(i => new SpecToken(i.Name, i.Supported)).ToList(),
                "Conjuntos de instruções que programas e jogos podem exigir. Os apagados este processador não tem."));

        return new Sheet
        {
            Key = Cpu,
            Eyebrow = c.Vendor,
            Title = GameCatalog.ShortCpuName(c.Name) is { Length: > 0 } shortName ? shortName : c.Name,
            Subtitle = c.Name,
            Readouts = new()
            {
                new("Núcleos", cores.ToString(Br), "", t.Hybrid ? $"{t.PerformanceCores} de desempenho + {t.EfficientCores} de eficiência" : null),
                new("Threads", threads.ToString(Br)),
                new("Clock base", c.BaseMhz > 0 ? N(c.BaseMhz / 1000.0, "0.0#") : "—", c.BaseMhz > 0 ? "GHz" : ""),
                new("Cache L3", l3Value, l3Unit),
            },
            Groups = groups,
            Link = new("Clocks, temperatura e consumo ao vivo em Sensores", "PageSensors:cpu"),
        };
    }

    // ---------------- Placa de vídeo ----------------

    private static Sheet GpuSheet(SystemInventory inv, IReadOnlyList<GpuInfo> gpus)
    {
        var adapters = inv.Gpus;
        if (adapters.FirstOrDefault() is not { } g) return Missing(Gpu, "Placa de vídeo", inv.Errors.GetValueOrDefault(SystemInventory.GpuTitle));
        var spec = gpus.FirstOrDefault(x => x.Name == g.Name);
        ulong vram = spec?.VramBytes > 0 ? spec.VramBytes : g.Vram ?? 0;
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
                    "Abaixo do máximo da tela. Ajuste em Configurações › Sistema › Tela › Tela avançada.")
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
            Title = ShortGpu(g.Name),
            Subtitle = g.Name,
            Readouts = new()
            {
                new("Memória", vValue, vUnit),
                new("Driver", g.DriverVersion),
                new("Resolução", primary != null ? $"{primary.Width} × {primary.Height}" : "—", "", inv.Displays.Count > 1 ? "Da tela principal" : null),
                new("Atualização", primary != null ? primary.Hz.ToString(Br) : "—", primary != null ? "Hz" : ""),
            },
            Groups = groups,
            Link = new("Especificações completas, clocks e leituras ao vivo em Placa de vídeo", "PageGpu"),
        };
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

    private static string ShortGpu(string name)
    {
        foreach (var p in new[] { "NVIDIA ", "AMD ", "Intel(R) ", "Intel " })
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return name[p.Length..].Replace("(TM)", "").Replace("(R)", "").Trim();
        return name;
    }

    // ---------------- Memória ----------------

    private static Sheet RamSheet(SystemInventory inv, MemoryInfo.Snapshot? memory, DateTime now)
    {
        if (inv.Ram is not { Modules.Count: > 0 } r) return Missing(Ram, "Memória RAM", inv.Errors.GetValueOrDefault(SystemInventory.RamTitle));
        var makers = r.Modules.Select(m => m.Maker).Where(m => m != "—").Distinct().ToList();
        var sizes = r.Modules.GroupBy(m => m.Capacity).Select(g => $"{g.Count()} × {Fmt.Bytes(g.Key)}");
        var partList = r.Modules.Select(m => m.Part).Where(p => p != "—").Distinct().ToList();
        string form = string.Join(" / ", r.Modules.Select(m => m.Form).Distinct());
        var volts = r.Modules.Select(m => m.Volt).Where(v => v != null).Distinct().ToList();

        // Velocidade, perfil e canais: o que mais muda o desempenho da RAM, com o veredito ao lado do valor.
        var speed = new List<SpecRow>
        {
            new("Em uso", r.Configured > 0 ? $"{r.Configured} MT/s" : "—", r.Configured > 0 ? $"{r.Configured / 2} MHz reais" : null,
                "MT/s são transferências por segundo. A DDR faz duas por ciclo, por isso o clock real é a metade."),
            new("Nominal (JEDEC)", r.Rated > 0 ? $"{r.Rated} MT/s" : "—", null, "Velocidade que o módulo informa ao Windows."),
        };
        if (r.PartSpeed > 0) speed.Add(new("Do part number", $"{r.PartSpeed} MT/s", null, "Velocidade escrita no código do modelo, a do perfil XMP/EXPO."));
        if (r.Profile is { } p)
        {
            var (value, help) = p.Tone switch
            {
                Tone.Ok when r.PartSpeed > 0 => ("Ativo", "A RAM roda na velocidade do part number, o que só acontece com o perfil ligado."),
                Tone.Ok => ("Não precisa", "A RAM já roda na velocidade máxima que informa."),
                Tone.Warn when p.Title.StartsWith("Abaixo") => ("Abaixo da nominal", $"Roda a {r.Configured} de {r.Rated} MT/s. Confira XMP/EXPO na BIOS."),
                Tone.Warn => ("Desligado", $"Roda a {r.Configured} MT/s, mas o part number indica {r.PartSpeed}. Ative o perfil na BIOS."),
                _ => ("Não identificado", "Confira na BIOS se o XMP/EXPO está ligado."),
            };
            speed.Add(Verdict("Perfil XMP/EXPO", value, p.Tone, p.Tone == Tone.Ok && r.PartSpeed > 0 ? "estimado" : null, help,
                "O Windows não informa se o perfil está ligado; a estimativa compara a velocidade em uso com a do módulo."));
        }
        if (r.ChannelFinding is { } ch)
        {
            string value = ch.Tone == Tone.Ok ? ch.Title.TrimEnd(':') : r.Channels == null && r.Modules.Count > 1 ? "Não identificado" : "Canal único";
            speed.Add(Verdict("Canais", value, ch.Tone, null,
                ch.Tone == Tone.Ok ? Cap(ch.Text.TrimEnd('.')) + ": o dobro de dados por vez com o processador." : Cap(ch.Text)));
        }

        var capacity = new List<SpecRow>
        {
            new("Instalada", Fmt.Bytes(r.Total)),
            new("Tipo", form.Length > 0 && form != "—" ? $"{r.Type} · {form}" : r.Type, form.Contains("SODIMM") || form.Contains("SO-DIMM") ? "de notebook" : null),
        };
        if (memory is { } mem)
        {
            double reserved = (double)r.Total - mem.Total;
            capacity.Add(new("Utilizável", Fmt.Bytes(mem.Total), reserved > 64 << 20 ? $"{Fmt.Bytes(reserved)} reservados" : null,
                "O que sobra para o Windows depois do que o firmware e o vídeo integrado separam."));
        }
        if (r.MaxCapacity > 0) capacity.Add(new("Máximo da placa", Fmt.Bytes(r.MaxCapacity), null, "Capacidade máxima que a placa-mãe declara suportar."));
        string? buffer = r.Modules.Select(m => m.Buffer).Distinct().SingleOrDefault();
        if (buffer != null) capacity.Add(new("Buffer", Cap(buffer), null, "Sem buffer é o módulo comum de PC; registrada é a de servidor."));
        capacity.Add(new("ECC", r.Ecc ? "Sim" : "Não", null, "Correção de erros por hardware, comum em servidores e estações de trabalho."));

        // Slots da placa, ocupados e vazios, com o canal de cada um.
        var map = r.SlotMap();
        string SlotName(RamSlot sl, int i) => sl.Name.Length > 0 ? sl.Name : $"Slot {i + 1}";
        var slotRows = map.Select((sl, i) => sl.Module is { } m
            ? new SpecRow(SlotName(sl, i), $"{Fmt.Bytes(m.Capacity)} {m.Type}" + (m.Configured > 0 ? $" · {m.Configured} MT/s" : ""), sl.Channel != null ? $"canal {sl.Channel}" : null,
                $"{m.Bank} · {m.Locator}")
            : new SpecRow(SlotName(sl, i), "Vazio", sl.Channel != null ? $"canal {sl.Channel}" : null)).ToList();

        // Módulos lado a lado: uma coluna por módulo, as linhas que os módulos informam ao Windows.
        var mods = map.Select((sl, i) => (Name: SlotName(sl, i), Module: sl.Module)).Where(x => x.Module != null).Select(x => (x.Name, Module: x.Module!)).ToList();
        SpecRow Cells(string key, Func<RamModule, string> cell, string? tip = null)
        {
            var cells = mods.Select(x => cell(x.Module)).ToList();
            return new SpecRow(key, string.Join(" / ", cells.Distinct()), null, tip) { Cells = cells };
        }
        var moduleRows = new List<SpecRow>
        {
            Cells("Fabricante", m => m.Maker),
            Cells("Part number", m => m.Part, "Código do modelo. Com ele dá para achar a ficha técnica e os timings no site da fabricante."),
            Cells("Velocidade", m => m.Configured > 0 ? $"{m.Configured} MT/s" : "—"),
            Cells("Ranks", m => m.Ranks switch { 1 => "1 · single", 2 => "2 · dual", 4 => "4 · quad", > 0 => m.Ranks.ToString(Br), _ => "—" },
                "Quantos conjuntos de chips o módulo tem. Dual rank costuma render um pouco mais em jogos."),
            Cells("Tensão", m => m.MinVolt is double lo && m.MaxVolt is double hi && lo != hi ? $"{Fmt.Volt(m.Volt)} ({N(lo, "0.0#")}–{N(hi, "0.0#")})" : Fmt.Volt(m.Volt),
                "Tensão em uso e, entre parênteses, a faixa que o módulo aceita."),
            // Placas que dão o mesmo nome a todos os slots (DIMM 1) se diferenciam pelo banco (P0 CHANNEL A).
            mods.Select(x => x.Module.Locator).Distinct().Count() == mods.Count
                ? Cells("Posição", m => m.Locator, "Nome que a placa-mãe dá ao slot.")
                : Cells("Posição", m => m.Bank, "Nome que a placa-mãe dá ao banco do slot."),
            Cells("Nº de série", m => m.Serial),
        };
        if (mods.Any(x => x.Module.TotalWidth > x.Module.DataWidth && x.Module.DataWidth > 0))
            moduleRows.Insert(5, Cells("Largura", m => m.DataWidth > 0 ? $"{m.DataWidth} / {m.TotalWidth} bits" : "—", "Dados / total; os bits a mais são do ECC."));
        moduleRows.RemoveAll(row => row.Cells!.All(c => c == "—"));

        var groups = new List<SpecGroup>
        {
            new("Velocidade e canais", speed),
            new("Capacidade", capacity),
            new("Slots", slotRows),
        };
        // Até dois módulos cabem lado a lado; com mais, cada um ganha o próprio grupo.
        if (mods.Count <= 2)
            groups.Add(new(mods.Count == 1 ? "Módulo" : "Módulos", moduleRows) { Columns = mods.Select(x => x.Name).ToList() });
        else
            for (int k = 0; k < mods.Count; k++)
                groups.Add(new($"Módulo {mods[k].Name}", moduleRows.Select(row => row with { Value = row.Cells![k], Cells = null }).ToList()));
        foreach (var pf in r.PageFiles)
            capacity.Add(new("Arquivo de paginação", $"{Fmt.Bytes(pf.AllocatedMb * 1048576.0)} em {pf.Path[..Math.Min(2, pf.Path.Length)]}",
                r.PageFileAuto == true ? "tamanho automático" : null, "Espaço no disco que o Windows usa como extensão da RAM quando ela enche."));

        var (rv, ru) = r.Configured > 0 ? (r.Configured.ToString(Br), "MT/s") : ("—", "");
        return new Sheet
        {
            Key = Ram,
            Eyebrow = string.Join(" · ", new[] { makers.Count == 1 ? makers[0] : null, $"{r.Modules.Count} {(r.Modules.Count == 1 ? "módulo" : "módulos")}" }.Where(x => x != null)),
            Title = $"{Fmt.Bytes(r.Total)} {r.Type}",
            Subtitle = string.Join(" · ", new[] { string.Join(" + ", sizes), partList.Count == 1 ? partList[0] : null, form }.Where(x => !string.IsNullOrEmpty(x) && x != "—")),
            Readouts = new()
            {
                new("Velocidade", rv, ru),
                new("Slots", r.Modules.Count.ToString(Br), r.Slots > 0 ? $"de {r.Slots}" : ""),
                new("Canais", r.Channels?.ToString(Br) ?? "—", "", r.Channels == null ? "Os nomes dos slots não dizem em que canal cada módulo está." : "Estimado pelos nomes dos slots."),
                new("Tensão", volts.Count == 1 ? N(volts[0]!.Value, "0.00#") : "—", volts.Count == 1 ? "V" : ""),
            },
            Groups = groups,
            Link = new("Temperatura dos módulos e memória em uso ao vivo em Sensores", "PageSensors:ram"),
        };
    }

    // ---------------- Placa-mãe e BIOS ----------------

    private static Sheet BoardSheet(SystemInventory inv, DateTime now)
    {
        if (inv.Board is not { } b) return Missing(Board, "Placa-mãe e BIOS", inv.Errors.GetValueOrDefault(SystemInventory.BoardTitle));
        var f = inv.Firmware;
        var board = new List<SpecRow> { new("Fabricante", b.Maker), new("Modelo", b.Model), new("Revisão", b.Revision) };
        if (inv.Ram is { Slots: > 0 } ram)
            board.Add(new("Slots de memória", ram.Slots.ToString(Br), ram.MaxCapacity > 0 ? $"até {Fmt.Bytes(ram.MaxCapacity)}" : null, $"{ram.Modules.Count} em uso"));
        if (inv.Cpus.FirstOrDefault() is { Socket: not "—" } cpu) board.Add(new("Soquete", cpu.Socket, null, "Encaixe do processador."));
        board.Add(new("Nº de série", b.Serial));
        if (b.Chassis != null) board.Add(new("Tipo de computador", b.Chassis, null, "O formato que o firmware declara."));
        var groups = new List<SpecGroup> { new("Placa-mãe", board) };
        if (f != null)
        {
            var bios = new List<SpecRow> { new("Fabricante", f.Vendor), new("Versão", f.Version), new("Data", Fmt.Date(f.Date), Fmt.Ago(f.Date, now)) };
            if (f.Smbios != null) bios.Add(new("SMBIOS", f.Smbios, null, "Versão da tabela de hardware que a BIOS entrega ao Windows."));
            groups.Add(new("BIOS", bios, Help: "O firmware da placa: liga o hardware antes do Windows. Atualizações trazem correções e suporte a processadores novos."));

            bool? tpmOk = f.Tpm == null ? null : f.Tpm.StartsWith("2") && f.TpmReady != false;
            string tpmValue = f.Tpm == null ? "Não lido" : f.TpmReady == false && f.Tpm != "Ausente" ? $"{f.Tpm} · desligado" : f.Tpm;
            static Tone T(bool? ok) => ok switch { true => Tone.Ok, false => Tone.Warn, _ => Tone.None };
            var security = new List<SpecRow>
            {
                Verdict("Modo de inicialização", f.Uefi switch { true => "UEFI", false => "Legado (CSM)", _ => "—" }, T(f.Uefi), null,
                    f.Uefi == false ? "O Windows 11 pede UEFI. Dá para converter o disco e mudar na BIOS." : null),
                Verdict("Secure Boot", Fmt.OnOff(f.SecureBoot), T(f.SecureBoot), null,
                    f.SecureBoot == false ? "Ative na BIOS. Alguns jogos com anti-cheat exigem." : null,
                    "Só deixa iniciar sistemas e drivers assinados."),
                Verdict("TPM", tpmValue, T(tpmOk), null,
                    f.Tpm == null ? "O TPM só é lido com o CoreBi aberto como administrador." : tpmOk == false ? "Ative fTPM (AMD) ou PTT (Intel) na BIOS." : null,
                    "Chip de segurança usado pelo BitLocker e pelo Windows Hello."),
                new("Tipo de TPM", f.TpmMaker switch
                {
                    "AMD" => "fTPM, no processador AMD",
                    "INTC" => "PTT, no chipset Intel",
                    "MSFT" => "Virtual (Microsoft)",
                    null => "—",
                    var other => $"Chip dedicado ({other})"
                }, null, "Onde o TPM mora: no próprio processador (firmware) ou num chip separado na placa."),
                new("Firmware do TPM", f.TpmFirmware ?? "—"),
                f.Windows11Ready switch
                {
                    true => Verdict("Requisitos do Windows 11", "Atendidos", Tone.Ok),
                    false => Verdict("Requisitos do Windows 11", "Falta um", Tone.Warn, null, "UEFI, Secure Boot e TPM 2.0 precisam estar ligados."),
                    _ => Verdict("Requisitos do Windows 11", "Não deu para conferir", Tone.None),
                },
            };
            groups.Add(new("Segurança", security, Help: "O que o Windows 11 exige do firmware para iniciar com segurança."));

        }
        // PCs montados repetem a placa aqui; o grupo só aparece quando diz algo novo (notebooks e PCs de marca).
        if ((b.SystemMaker != null || b.SystemModel != null) && b.SystemModel != b.Model)
            groups.Add(new("Computador", new() { new("Fabricante", b.SystemMaker ?? "—"), new("Modelo", b.SystemModel ?? "—") }));

        string biosMonth = f?.Date is DateTime d ? d.ToString("MM/yyyy", CultureInfo.InvariantCulture) : "—";
        return new Sheet
        {
            Key = Board,
            Eyebrow = b.Maker != "—" ? Fmt.ShortMaker(b.Maker) : b.SystemMaker != null ? Fmt.ShortMaker(b.SystemMaker) : "",
            Title = b.Model != "—" ? b.Model : b.SystemModel ?? "Placa-mãe",
            Subtitle = string.Join(" · ", new[] { b.Maker, b.Revision != "—" ? $"revisão {b.Revision}" : null }.Where(x => x != null && x != "—")),
            Readouts = new()
            {
                new("BIOS", f?.Version ?? "—"),
                new("Lançada em", biosMonth, "", f?.Date != null ? Fmt.Ago(f.Date, now) : null),
                new("Firmware", f?.Uefi switch { true => "UEFI", false => "Legado", _ => "—" }),
                new("TPM", f?.Tpm is { } tpm && tpm != "Ausente" ? tpm : "—"),
            },
            Groups = groups,
            Link = new("Temperaturas, ventoinhas e tensões ao vivo em Sensores", "PageSensors:board"),
        };
    }

    // ---------------- Armazenamento ----------------

    private static Sheet StorageSheet(SystemInventory inv, IReadOnlyList<DriveCard> drives)
    {
        var disks = new List<DiskView>();
        var groups = new List<SpecGroup>();
        foreach (var d in inv.Disks)
        {
            var vols = drives.Where(v => v.Disk.Length > 0 && v.Disk == d.Model).ToList();
            double size = Math.Max(1, d.Size);
            var volumes = vols.Select(v => new VolumeView(v.Letter, (string.IsNullOrWhiteSpace(v.Label) ? v.Kind : v.Label) + (v.FileSystem.Length > 0 ? $" · {v.FileSystem}" : ""),
                $"{v.UsedPercent} usado de {DriveCard.Fmt(v.Total)}", Math.Max(0.04, v.Total / size), v.UsedFraction,
                $"{DriveCard.Fmt(v.Free)} livres")).ToList();
            var (sv, su) = Split(Fmt.Bytes(d.Size));
            string meta = string.Join(" · ", new[] { $"Disco {d.Index}", d.System ? "disco do Windows" : "", d.Removable && d.Bus != "USB" ? "removível" : "" }.Where(x => x.Length > 0));
            var tone = d.Health switch { "Saudável" => Tone.Ok, null => Tone.None, _ => Tone.Warn };
            string freeText = vols.Count > 0 ? "" : "sem unidade montada";
            disks.Add(new DiskView(d.Model, meta, sv, su, freeText, volumes, d.Health ?? "", tone) { First = disks.Count == 0 });
            var (about, layout) = DiskRows(d, vols, inv.BitLocker);
            // O nome do disco já está na linha de cima; os grupos levam só o número dele quando há mais de um.
            bool many = inv.Disks.Count > 1;
            groups.Add(new(many ? $"Especificações do disco {d.Index}" : "Especificações", about));
            if (layout.Count > 0) groups.Add(new(many ? $"Partições e proteção do disco {d.Index}" : "Partições e proteção", layout));
        }
        double total = inv.Disks.Sum(d => (double)d.Size);
        double freeAll = drives.Sum(v => (double)v.Free), usedAll = drives.Sum(v => (double)(v.Total - v.Free));
        var (uv, uu) = Split(Fmt.Bytes(usedAll));
        var (fv, fu) = Split(Fmt.Bytes(freeAll));
        return new Sheet
        {
            Key = Storage,
            Eyebrow = "Capacidade total",
            Title = total > 0 ? Fmt.Bytes(total) : "—",
            // Do que é feito o armazenamento ("SSD NVMe · HD SATA"); contagens e espaço ficam nas leituras.
            Subtitle = string.Join(" · ", inv.Disks.Select(d => string.Join(" ", new[] { d.Media, d.Bus }.Where(x => x.Length > 0 && x != "—"))).Where(x => x.Length > 0).Distinct()),
            Readouts = new()
            {
                new("Em uso", uv, uu),
                new("Livre", fv, fu),
                new("Discos", inv.Disks.Count.ToString(Br)),
                new("Unidades", drives.Count.ToString(Br)),
            },
            Disks = disks,
            Groups = groups,
            Link = new("Saúde, temperatura e vida útil em Discos", "PageDisks"),
        };
    }

    /// <summary>Ficha de um disco: o que ele é, como está ligado e o estado que o Windows informa.</summary>
    private static (List<SpecRow> About, List<SpecRow> Layout) DiskRows(PhysicalDiskInfo d, List<DriveCard> vols, Dictionary<string, bool> bitLocker)
    {
        string media = d.Media switch { "SSD" => "SSD", "HDD" => "HD (disco giratório)", var m => m };
        var rows = new List<SpecRow>
        {
            new("Interface", d.Bus, null, d.Bus switch
            {
                "NVMe" => "Ligado direto nas linhas PCIe do processador ou do chipset: o tipo mais rápido.",
                "SATA" => "Conexão SATA: até uns 550 MB/s, o limite dos SSDs SATA.",
                "USB" => "Disco externo pela USB.",
                _ => null
            }),
            new("Tipo", media),
        };
        if (d.Rpm is int rpm) rows.Add(new("Rotação", $"{rpm} rpm", null, "Quanto mais rápido o prato gira, menor o tempo de acesso."));
        rows.Add(new("Firmware", d.Firmware, null, "Versão do software interno do disco. Fabricantes às vezes lançam correções."));
        // NVMe: o Windows entrega o identificador com grupos de zeros na frente (0000_0000_..._707C_18B5); a parte útil é o fim.
        string serial = d.Serial.Trim().TrimEnd('.');
        string shortSerial = System.Text.RegularExpressions.Regex.Replace(serial, "^(0000_)+", "");
        rows.Add(new("Nº de série", shortSerial, null, shortSerial != serial ? $"Identificador completo: {serial}" : null));
        var layout = new List<SpecRow>();
        if (d.Partition != null)
            layout.Add(new("Tabela de partições", d.Partition, d.Partitions > 0 ? $"{d.Partitions} {(d.Partitions == 1 ? "partição" : "partições")}" : null,
                "GPT é o padrão atual e é o que o Windows 11 usa para iniciar em UEFI; MBR é o formato antigo, limitado a 2 TB. " +
                "Além das unidades com letra, o Windows guarda partições ocultas de inicialização e recuperação."));
        if (d.LogicalSector > 0)
            layout.Add(new("Setor", d.PhysicalSector > d.LogicalSector ? $"{d.LogicalSector} B lógico · {d.PhysicalSector / 1024} KB físico" : $"{d.LogicalSector} B",
                null, "O menor bloco que o disco lê ou grava."));
        var locked = vols.Where(v => bitLocker.TryGetValue(v.Letter.TrimEnd('\\'), out _)).ToList();
        if (locked.Count > 0)
        {
            var on = locked.Where(v => bitLocker[v.Letter.TrimEnd('\\')]).Select(v => v.Letter.TrimEnd('\\')).ToList();
            layout.Add(new("BitLocker", on.Count > 0 ? $"Ativado em {string.Join(", ", on)}" : "Desativado", null,
                "Criptografia do Windows: sem a senha ou a chave de recuperação, ninguém lê os dados do disco."));
        }
        return (rows, layout);
    }

    // ---------------- Sistema operacional ----------------

    private static Sheet OsSheet(SystemInventory inv, DateTime now)
    {
        if (inv.Os is not { } o) return Missing(Os, "Sistema operacional", inv.Errors.GetValueOrDefault(SystemInventory.OsTitle));
        string up = o.LastBoot is DateTime boot ? Fmt.Span(now - boot) : "—";
        var (upValue, upUnit) = SplitSpan(up);
        var windows = new List<SpecRow>
        {
            new("Edição", o.Name),
            new("Versão", o.Version, null, "A atualização anual de recursos (23H2 = segundo semestre de 2023)."),
            new("Build", o.Build, null, null, Tone.None, "O número depois do ponto sobe a cada atualização mensal. Bom para conferir se o Windows está em dia."),
            new("Arquitetura", o.Architecture),
            new("Idioma", Cap(o.Language)),
        };
        var computer = new List<SpecRow> { new("Nome", o.Machine, null, "Como o PC aparece na rede."), new("Usuário", o.User), new(o.Domain ? "Domínio" : "Grupo de trabalho", o.Workgroup) };
        var session = new List<SpecRow> { new("Instalado em", Fmt.Date(o.Installed), Fmt.Ago(o.Installed, now)), new("Último boot", Fmt.Stamp(o.LastBoot)), new("Ligado há", up) };
        if (o.LastUpdate != null) session.Insert(1, new("Última atualização", Fmt.Date(o.LastUpdate), o.LastUpdateId, "A atualização mais recente instalada pelo Windows Update."));
        var groups = new List<SpecGroup> { new("Windows", windows), new("Computador", computer), new("Sessão", session) };
        if (inv.Settings is { } st)
        {
            computer.Add(new("Fuso horário", st.TimeZone));
            if (st.Activated != null) windows.Add(Verdict("Ativação", st.Activated == true ? "Ativado" : "Não ativado", st.Activated == true ? Tone.Ok : Tone.Warn, null,
                st.Activated == true ? null : "Ative em Configurações > Sistema > Ativação."));
            groups.Add(new("Ajustes para jogos", new()
            {
                Verdict("Modo de jogo", st.GameMode ? "Ativado" : "Desativado", st.GameMode ? Tone.Ok : Tone.Warn, null,
                    st.GameMode ? null : "Ative em Configurações > Jogos > Modo de jogo.",
                    "Dá prioridade ao jogo e segura atualizações e notificações enquanto ele roda."),
                new("Agendamento de GPU por hardware", Fmt.OnOff(st.GpuScheduling), null,
                    "A placa de vídeo organiza a própria fila de trabalho. É exigido pela geração de quadros do DLSS. Fica em Configurações > Sistema > Tela > Elementos gráficos."),
                new("Integridade da memória", st.MemoryIntegrity ? "Ativada" : "Desativada", null,
                    "Proteção do Windows contra drivers maliciosos (isolamento de núcleo). Pode custar um pouco de desempenho em jogos."),
                new("Inicialização rápida", Fmt.OnOff(st.FastStartup), null,
                    "Ao desligar, o Windows salva parte do sistema para ligar mais rápido. Por isso o \"Ligado há\" pode ser longo mesmo desligando todo dia."),
                new("Plano de energia", st.PowerPlan ?? "—", null, "Quanto o Windows deixa o processador acelerar e quão rápido ele volta a economizar."),
                new("Captura em segundo plano", st.BackgroundCapture switch { true => "Ativada", false => "Desativada", _ => "—" }, null,
                    "Grava os últimos segundos de jogo o tempo todo, para salvar um clipe depois. Custa um pouco de desempenho."),
                new("Aceleração do mouse", st.MouseAcceleration switch { true => "Ativada", false => "Desativada", _ => "—" }, null,
                    "\"Aumentar a precisão do ponteiro\": o cursor anda mais quando o mouse se move rápido. Em jogos de tiro, muitos preferem desligada."),
            }, Help: "Opções do Windows que mudam como os jogos rodam. Passe o mouse em cada uma para saber o que faz.")
            { Column = 2 });
        }
        if (inv.Security is { } sec)
        {
            static string On(bool? v) => v switch { true => "Ativada", false => "Desativada", _ => "—" };
            var security = new List<SpecRow>();
            if (sec.Antivirus != null)
                security.Add(Verdict("Antivírus", sec.Antivirus.Length > 0 ? sec.Antivirus : "Nenhum ligado", sec.Antivirus.Length > 0 ? Tone.Ok : Tone.Warn, null,
                    sec.Antivirus.Length > 0 ? null : "Ligue a proteção em tempo real em Segurança do Windows."));
            security.Add(new("Proteção em tempo real", On(sec.RealTime), null, "Verifica arquivos e programas no momento em que são abertos."));
            security.Add(new("Proteção contra violação", On(sec.Tamper), null, "Impede que programas desliguem o antivírus por conta própria."));
            if (sec.Signatures is DateTime sig) security.Add(new("Definições de vírus", Fmt.Date(sig), Fmt.Ago(sig, now), "Quando o antivírus baixou a lista de ameaças mais recente."));
            security.Add(new("Controle de conta (UAC)", sec.Uac ?? "—", null, "O aviso que aparece quando um programa pede permissão de administrador."));
            groups.Add(new("Segurança do Windows", security) { Column = 0 });
        }
        // Três colunas: Windows e Segurança | Computador e Sessão | Ajustes para jogos.
        foreach (var (title, column) in new[] { ("Windows", 0), ("Computador", 1), ("Sessão", 1) })
            if (groups.FindIndex(g => g.Title == title) is int at and >= 0) groups[at] = groups[at] with { Column = column };

        string arch = o.Architecture.StartsWith("64") ? "64" : o.Architecture.StartsWith("32") ? "32" : "—";
        return new Sheet
        {
            Key = Os,
            Eyebrow = "Microsoft",
            Title = o.Name,
            Subtitle = o.Language.Length > 0 ? $"Em {o.Language}" : "",
            Readouts = new()
            {
                new("Versão", o.Version),
                new("Build", o.Build),
                new("Arquitetura", arch, arch != "—" ? "bits" : ""),
                new("Ligado há", upValue, upUnit),
            },
            Groups = groups,
        };
    }

    private static string Cap(string s) => s.Length > 0 ? char.ToUpper(s[0], Br) + s[1..] : s;

    private static Sheet Missing(string key, string title, string? error) => new()
    {
        Key = key,
        Title = title,
        Subtitle = error != null ? $"Não foi possível ler: {error}" : "Nada encontrado",
    };

    /// <summary>"32 GB" → ("32", "GB").</summary>
    private static (string, string) Split(string s)
    {
        int i = s.LastIndexOf(' ');
        return i > 0 ? (s[..i], s[(i + 1)..]) : (s, "");
    }

    /// <summary>"4 h 32 min" → ("4 h 32", "min"); "2 d 5 h" → ("2 d 5", "h").</summary>
    private static (string, string) SplitSpan(string s) => Split(s);
}
