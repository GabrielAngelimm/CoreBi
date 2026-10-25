using System.Globalization;
using CoreBi.Engine.Games;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Storage;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

// ---------------- Modelos da ficha ----------------

/// <summary>Célula do índice: categoria, o nome do componente e uma linha de especificação.</summary>
public sealed record IndexItem(string Key, string Glyph, string Category, string Name, string Spec, bool Warn, string? WarnTip);

public sealed record Readout(string Label, string Value, string Unit = "", string? Tip = null);

/// <summary>Linha chave/valor; Note aparece em cinza depois do valor ("há 1 ano"). Clicar copia o valor.</summary>
public sealed record SpecRow(string Key, string Value, string? Note = null, string? Tip = null);

/// <summary>Grupo de linhas. Tokens: lista em linha (instruções do processador), apagadas quando não suportadas.</summary>
public sealed record SpecGroup(string Title, List<SpecRow> Rows, List<SpecToken>? Tokens = null);

public sealed record SpecToken(string Text, bool On);

public sealed record NoteItem(Tone Tone, string Title, string Text);

public sealed record LinkItem(string Text, string Page);

/// <summary>Tabela compacta (módulos de memória): cabeçalho e linhas com o mesmo número de células.</summary>
public sealed record SpecTable(string Title, List<string> Header, List<List<string>> Rows);

public sealed class Sheet
{
    public string Key { get; init; } = "";
    public string Eyebrow { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public List<Readout> Readouts { get; init; } = new();
    public string? FigureTitle { get; init; }
    public object? Figure { get; init; }
    public List<NoteItem> Notes { get; init; } = new();
    public List<SpecGroup> Groups { get; init; } = new();
    public SpecTable? Table { get; init; }
    public string? Footnote { get; init; }
    public LinkItem? Link { get; init; }
    public List<DiskView>? Disks { get; init; }
}

// Figuras (cada uma tem um DataTemplate em SystemView.xaml)

public sealed record CoreTile(string Label, List<bool> Threads, bool Efficient, string Tip);
public sealed record CpuFigure(List<CoreTile> Cores, int Columns, double TileHeight, string L3, string ThreadLabel, string Legend, bool Hybrid);

public sealed record SlotView(string Name, bool Filled, string Capacity, string Maker, string Speed, string Tip);
public sealed record ChannelView(string Label, List<SlotView> Slots);
public sealed record RamFigure(List<ChannelView> Channels);

public sealed record ScreenView(string Number, string Monitor, string Resolution, string Detail, double Width, double Height, bool Primary, bool Warn);
public sealed record ScreenFigure(List<ScreenView> Screens, string Empty);

public sealed record ChainStep(bool? Ok, string Title, string Text, string Value, bool Last = false);
public sealed record ChainFigure(List<ChainStep> Steps);

public sealed record TimelineEvent(string Label, string Value, string Note, bool Now);
public sealed record TimelineFigure(List<TimelineEvent> Events);

public sealed record VolumeView(string Letter, string Name, string Line, double Weight, double Used, string Tip);
public sealed record DiskView(string Title, string Meta, List<string> Chips, string Size, string Unit, string Free, List<VolumeView> Volumes);

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
        SystemInventory inv, IReadOnlyList<GpuInfo> gpus, IReadOnlyList<DriveCard> drives, DateTime now)
    {
        var sheets = new Dictionary<string, Sheet>
        {
            [Cpu] = CpuSheet(inv),
            [Gpu] = GpuSheet(inv, gpus),
            [Ram] = RamSheet(inv),
            [Board] = BoardSheet(inv, now),
            [Storage] = StorageSheet(inv, drives),
            [Os] = OsSheet(inv, now),
        };
        var index = new List<IndexItem>();
        void Add(string key, string glyph, string category, string? error)
        {
            var s = sheets[key];
            var warn = s.Notes.FirstOrDefault(n => n.Tone == Tone.Warn);
            string spec = error != null ? "Não foi possível ler" : IndexSpec(key, s);
            index.Add(new IndexItem(key, glyph, category, error != null ? "—" : IndexName(key, s), spec, warn != null,
                warn != null ? $"{warn.Title} {warn.Text}" : null));
        }
        Add(Cpu, "", "Processador", inv.Errors.GetValueOrDefault(SystemInventory.CpuTitle));
        Add(Gpu, "", "Placa de vídeo", inv.Errors.GetValueOrDefault(SystemInventory.GpuTitle));
        Add(Ram, "", "Memória RAM", inv.Errors.GetValueOrDefault(SystemInventory.RamTitle));
        Add(Board, "", "Placa-mãe e BIOS", inv.Errors.GetValueOrDefault(SystemInventory.BoardTitle));
        Add(Storage, "", "Armazenamento", inv.Errors.GetValueOrDefault(SystemInventory.StorageTitle));
        Add(Os, "", "Sistema operacional", inv.Errors.GetValueOrDefault(SystemInventory.OsTitle));
        return (index, sheets);
    }

    private static string IndexName(string key, Sheet s) => key switch
    {
        Storage => s.Disks is { Count: > 0 } ? $"{s.Title} em {s.Disks.Count} {(s.Disks.Count == 1 ? "disco" : "discos")}" : s.Title,
        _ => s.Title
    };

    /// <summary>Linha curta do índice: as leituras da ficha que identificam o componente.</summary>
    private static string IndexSpec(string key, Sheet s)
    {
        string R(int i) => i < s.Readouts.Count ? (s.Readouts[i].Value + (s.Readouts[i].Unit.Length > 0 ? " " + s.Readouts[i].Unit : "")).Trim() : "";
        return key switch
        {
            Cpu => string.Join(" · ", new[] { $"{R(0)} núcleos", $"{R(1)} threads", R(2) }.Where(x => !x.StartsWith("—"))),
            Gpu => s.Subtitle.Split(" · ").LastOrDefault(p => p.Contains("GB")) is { } mem ? $"{mem} · driver {R(1)}" : $"driver {R(1)}",
            Ram => string.Join(" · ", new[] { R(0), s.Notes.Any(n => n.Title.StartsWith("Dual")) ? "dual channel" : null }.Where(x => !string.IsNullOrEmpty(x) && x != "—")),
            Board => string.Join(" · ", new[] { s.Eyebrow, $"BIOS {R(0)}" }.Where(x => x.Length > 0 && x != "—")),
            Storage => s.Readouts.Count > 1 ? $"{R(1)} livres" : "",
            Os => $"{R(0)} · build {R(1)}",
            _ => s.Subtitle
        };
    }

    // ---------------- Processador ----------------

    private static Sheet CpuSheet(SystemInventory inv)
    {
        if (inv.Cpus.FirstOrDefault() is not { } c) return Missing(Cpu, "Processador", inv.Errors.GetValueOrDefault(SystemInventory.CpuTitle));
        var t = c.Topology;
        int cores = t.Cores.Count > 0 ? t.Cores.Count : c.Cores, threads = t.Threads > 0 ? t.Threads : c.Threads;
        if (inv.Cpus.Count > 1) { cores = inv.Cpus.Sum(x => x.Cores); threads = inv.Cpus.Sum(x => x.Threads); }
        var l3 = t.Caches.Where(x => x.Level == 3).ToList();
        string l3Text = l3.Count > 0 ? Fmt.CacheGroup(l3) : c.L3Kb > 0 ? Fmt.Cache(c.L3Kb * 1024) : "—";
        var (l3Value, l3Unit) = l3.Count > 0 ? Split(Fmt.Cache(l3.Sum(x => x.Size * x.Instances))) : c.L3Kb > 0 ? Split(Fmt.Cache(c.L3Kb * 1024)) : ("—", "");
        bool smt = threads > cores;

        var id = new List<SpecRow>
        {
            new("Fabricante", c.Vendor),
            new("Nome completo", c.Name),
        };
        if (c.Family != null) id.Add(new("Família · modelo · stepping", $"{c.Family} · {c.Model} · {c.Stepping}", null,
            $"Em hexadecimal: família {c.Family:X}h, modelo {c.Model:X}h. É o que identifica a geração exata do chip."));
        id.Add(new("Arquitetura", c.Architecture));
        id.Add(new("Soquete", c.Socket));
        if (inv.Cpus.Count > 1) id.Add(new("Processadores", inv.Cpus.Count.ToString(Br)));

        var clocks = new List<SpecRow> { new("Núcleos", cores.ToString(Br)) };
        if (t.Hybrid) clocks.Add(new("Desempenho + eficiência", $"{t.PerformanceCores} + {t.EfficientCores}", null,
            "Núcleos de desempenho (P) rodam jogos e tarefas pesadas; os de eficiência (E) ficam com o que roda em segundo plano."));
        clocks.Add(new("Threads", threads.ToString(Br), smt ? "SMT ativado" : "sem SMT"));
        clocks.Add(new("Clock base", Fmt.Ghz(c.BaseMhz), null, "Frequência declarada pelo fabricante. Em uso o processador sobe acima dela (boost) conforme temperatura e consumo; a leitura ao vivo fica em Sensores."));

        var cache = Fmt.CacheLines(c).Select(kv => new SpecRow(kv.Key, kv.Value, kv.Key == "L3" && l3.Count == 1 ? "compartilhado" : null)).ToList();
        var groups = new List<SpecGroup> { new("Identificação", id), new("Núcleos e clocks", clocks) };
        if (cache.Count > 0) groups.Add(new("Cache", cache));
        if (c.Instructions.Count > 0)
            groups.Add(new("Instruções", new(), c.Instructions.Select(i => new SpecToken(i.Name, i.Supported)).ToList()));

        // Figura: um ladrilho por núcleo com as threads dele; L3 compartilhado embaixo.
        CpuFigure? fig = null;
        if (t.Cores.Count is > 0 and <= 64)
        {
            int pClass = t.Cores.Max(x => x.EfficiencyClass);
            var tiles = t.Cores.Select((k, i) =>
            {
                bool eff = t.Hybrid && k.EfficiencyClass != pClass;
                string kind = t.Hybrid ? (eff ? "E" : "P") : "";
                return new CoreTile($"{kind}{i}", Enumerable.Range(0, Math.Min(k.Threads, 4)).Select(_ => true).ToList(), eff,
                    $"Núcleo {i}{(t.Hybrid ? eff ? " · eficiência" : " · desempenho" : "")} · {k.Threads} {(k.Threads == 1 ? "thread" : "threads")}");
            }).ToList();
            var l1d = t.Cache(1, "dados"); var l1i = t.Cache(1, "instruções"); var l2 = t.Caches.FirstOrDefault(x => x.Level == 2);
            string legend = l1d != null && l2 != null
                ? $"cada núcleo: L1 {Fmt.Cache(l1d.Size)} + {Fmt.Cache(l1i?.Size ?? 0)} · L2 {Fmt.Cache(l2.Size)}" + (l2.Instances < t.Cores.Count ? " (por grupo)" : "")
                : "";
            int cols = tiles.Count switch { <= 4 => tiles.Count, <= 8 => 4, <= 12 => 6, _ => 8 };
            int rows = (tiles.Count + cols - 1) / cols;
            fig = new CpuFigure(tiles, cols, rows <= 2 ? 92 : rows == 3 ? 70 : 54, l3Text, t.Hybrid ? "thread de núcleo de desempenho" : "thread", legend, t.Hybrid);
        }

        var notes = new List<NoteItem>();
        if (c.VirtualizationFinding is { } v) notes.Add(new(v.Tone, v.Title, v.Text));

        return new Sheet
        {
            Key = Cpu,
            Eyebrow = c.Vendor,
            Title = GameCatalog.ShortCpuName(c.Name) is { Length: > 0 } shortName ? shortName : c.Name,
            Subtitle = string.Join(" · ", new[] { c.Name, c.Socket != "—" ? $"soquete {c.Socket}" : null }.Where(x => x != null)),
            Readouts = new()
            {
                new("Núcleos", cores.ToString(Br), "", t.Hybrid ? $"{t.PerformanceCores} de desempenho + {t.EfficientCores} de eficiência" : null),
                new("Threads", threads.ToString(Br)),
                new("Clock base", c.BaseMhz > 0 ? N(c.BaseMhz / 1000.0, "0.0#") : "—", c.BaseMhz > 0 ? "GHz" : ""),
                new("Cache L3", l3Value, l3Unit),
            },
            FigureTitle = fig != null ? "Núcleos e cache" : null,
            Figure = fig,
            Notes = notes,
            Groups = groups,
            Link = new("Clocks, temperatura e consumo ao vivo em Sensores", "PageSensors"),
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

        var id = new List<SpecRow> { new("Nome", g.Name), new("Fabricante do chip", g.Vendor) };
        // Chip, arquitetura e tipo de memória vêm do banco de especificações da página Placa de vídeo.
        var parts = spec?.Subtitle.Split("  ·  ", StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        if (parts.Length >= 2) id.Add(new("Chip", $"{parts[0]} · {parts[1]}"));
        if (vram > 0) id.Add(new("Memória", parts.FirstOrDefault(p => p.Contains(" GB")) ?? Fmt.Vram(vram)));

        var driver = new List<SpecRow> { new("Versão", g.DriverVersion) };
        if (g.WindowsDriverVersion != g.DriverVersion) driver.Add(new("Versão no Windows", g.WindowsDriverVersion));
        driver.Add(new("Data", Fmt.Date(g.DriverDate), Fmt.Ago(g.DriverDate)));

        var groups = new List<SpecGroup> { new("Identificação", id), new("Driver", driver) };
        foreach (var d in inv.Displays)
        {
            var rows = new List<SpecRow>
            {
                new("Monitor", d.Monitor),
                new("Resolução", $"{d.Width} × {d.Height}"),
                new("Atualização", $"{d.Hz} Hz", d.BelowMax ? $"aceita até {d.MaxHz} Hz" : null),
                new("Cores", $"{d.Bits} bits"),
            };
            if (d.Inches is double inch) rows.Insert(1, new("Tamanho", $"{inch:0}\""));
            if (adapters.Count > 1) rows.Add(new("Placa de vídeo", d.Adapter));
            groups.Add(new(inv.Displays.Count > 1 ? $"Tela {d.Number}" + (d.Primary ? " (principal)" : "") : "Tela", rows));
        }
        if (adapters.Count > 1)
            groups.Add(new("Outras placas de vídeo", adapters.Skip(1).Select(a =>
                new SpecRow(a.Name, string.Join(" · ", new[] { a.Vram is ulong v && v > 0 ? Fmt.Vram(v) : null, $"driver {a.DriverVersion}" }.Where(x => x != null)))).ToList()));

        // Figura: as telas na posição em que o Windows as organiza, em escala.
        var screens = new List<ScreenView>();
        if (inv.Displays.Count > 0)
        {
            int minX = inv.Displays.Min(d => d.X), maxX = inv.Displays.Max(d => d.X + d.Width);
            int minY = inv.Displays.Min(d => d.Y), maxY = inv.Displays.Max(d => d.Y + d.Height);
            double scale = Math.Min(340.0 / Math.Max(1, maxX - minX), 240.0 / Math.Max(1, maxY - minY));
            scale = Math.Min(scale, 340.0 / 1920);
            foreach (var d in inv.Displays.OrderBy(d => d.X))
                screens.Add(new ScreenView(d.Number.ToString(Br), d.Inches is double inch ? $"{d.Monitor} · {inch:0}\"" : d.Monitor,
                    $"{d.Width} × {d.Height}", $"{d.Hz} Hz · {d.Bits} bits", Math.Max(90, d.Width * scale), Math.Max(56, d.Height * scale), d.Primary, d.BelowMax));
        }

        var notes = new List<NoteItem>();
        foreach (var d in inv.Displays.Where(d => d.BelowMax))
            notes.Add(new(Tone.Warn, inv.Displays.Count > 1 ? $"Tela {d.Number} abaixo do máximo:" : "Tela abaixo do máximo:",
                $"roda a {d.Hz} Hz, mas aceita {d.MaxHz} Hz nessa resolução. Ajuste em Configurações › Sistema › Tela › Tela avançada."));

        return new Sheet
        {
            Key = Gpu,
            Eyebrow = g.Vendor,
            Title = ShortGpu(g.Name),
            Subtitle = spec?.Subtitle.Replace("  ·  ", " · ") is { Length: > 0 } sub ? sub : g.Name,
            Readouts = new()
            {
                new("Memória", vValue, vUnit),
                new("Driver", g.DriverVersion),
                new("Resolução", primary != null ? $"{primary.Width} × {primary.Height}" : "—", "", inv.Displays.Count > 1 ? "Da tela principal" : null),
                new("Atualização", primary != null ? primary.Hz.ToString(Br) : "—", primary != null ? "Hz" : ""),
            },
            FigureTitle = inv.Displays.Count > 1 ? "Telas conectadas" : "Tela",
            Figure = new ScreenFigure(screens, "Nenhuma tela ligada à área de trabalho"),
            Notes = notes,
            Groups = groups,
            Link = new("Especificações completas, clocks e leituras ao vivo em Placa de vídeo", "PageGpu"),
        };
    }

    private static string ShortGpu(string name)
    {
        foreach (var p in new[] { "NVIDIA ", "AMD ", "Intel(R) ", "Intel " })
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return name[p.Length..].Replace("(TM)", "").Replace("(R)", "").Trim();
        return name;
    }

    // ---------------- Memória ----------------

    private static Sheet RamSheet(SystemInventory inv)
    {
        if (inv.Ram is not { Modules.Count: > 0 } r) return Missing(Ram, "Memória RAM", inv.Errors.GetValueOrDefault(SystemInventory.RamTitle));
        var makers = r.Modules.Select(m => m.Maker).Where(m => m != "—").Distinct().ToList();
        var sizes = r.Modules.GroupBy(m => m.Capacity).Select(g => $"{g.Count()} × {Fmt.Bytes(g.Key)}");
        var partList = r.Modules.Select(m => m.Part).Where(p => p != "—").Distinct().ToList();
        string form = string.Join(" / ", r.Modules.Select(m => m.Form).Distinct());
        var volts = r.Modules.Select(m => m.Volt).Where(v => v != null).Distinct().ToList();
        var minV = r.Modules.Select(m => m.MinVolt).Where(v => v != null).DefaultIfEmpty(null).Min();
        var maxV = r.Modules.Select(m => m.MaxVolt).Where(v => v != null).DefaultIfEmpty(null).Max();

        var config = new List<SpecRow>
        {
            new("Total", Fmt.Bytes(r.Total)),
            new("Módulos", r.Slots > 0 ? $"{r.Modules.Count} de {r.Slots} slots" : r.Modules.Count.ToString(Br)),
        };
        if (r.MaxCapacity > 0) config.Add(new("Máximo da placa", Fmt.Bytes(r.MaxCapacity), null, "Capacidade máxima que a placa-mãe declara suportar."));
        config.Add(new("Tipo", r.Type));
        config.Add(new("Formato", form));
        config.Add(new("ECC", r.Ecc ? "Sim" : "Não", null, "Correção de erros por hardware, comum em servidores e estações de trabalho."));

        var speed = new List<SpecRow>
        {
            new("Em uso", r.Configured > 0 ? $"{r.Configured} MT/s" : "—"),
            new("Nominal (JEDEC)", r.Rated > 0 ? $"{r.Rated} MT/s" : "—", null, "Velocidade padrão gravada no módulo, a que a placa usa sem perfil XMP/EXPO."),
        };
        if (r.PartSpeed > 0) speed.Add(new("Indicada no part number", $"{r.PartSpeed} MT/s"));
        speed.Add(new("Tensão em uso", volts.Count > 0 ? string.Join(" / ", volts.Select(Fmt.Volt)) : "—",
            minV != null && maxV != null && minV != maxV ? $"faixa {N(minV.Value, "0.0#")} – {N(maxV.Value, "0.0#")} V" : null));
        if (r.VirtualTotal > 0) speed.Add(new("Memória virtual", Fmt.Bytes(r.VirtualTotal), "RAM + paginação"));

        var table = new SpecTable("Módulos",
            new() { "Slot", "Capacidade", "Fabricante", "Part number", "Velocidade", "Ranks", "Tensão", "Nº de série" },
            r.Modules.OrderBy(m => m.Slot).Select(m => new List<string>
            {
                m.Slot, Fmt.Bytes(m.Capacity), m.Maker, m.Part, m.Configured > 0 ? $"{m.Configured} MT/s" : "—",
                m.Ranks > 0 ? m.Ranks.ToString(Br) : "—", Fmt.Volt(m.Volt), m.Serial
            }).ToList());

        // Figura: os slots por canal, com os vazios desenhados tracejados.
        var map = r.SlotMap();
        var channels = map.GroupBy(s => s.Channel).Select(g => new ChannelView(
            g.Key != null ? $"Canal {g.Key}" : map.Any(s => s.Channel != null) ? "Outros" : "Slots",
            g.Select(s => s.Module is { } m
                ? new SlotView(s.Name, true, Fmt.Bytes(m.Capacity), m.Maker != "—" ? m.Maker : m.Type,
                    m.Configured > 0 ? $"{m.Configured} MT/s" : "", $"{m.Bank} / {m.Locator} · {m.Part}")
                : new SlotView(s.Name, false, "", "", "", "Slot vazio")).ToList())).ToList();

        var notes = new List<NoteItem>();
        if (r.Profile is { } p) notes.Add(new(p.Tone, p.Title, p.Text));
        if (r.ChannelFinding is { } ch) notes.Add(new(ch.Tone, ch.Title, ch.Text));

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
            FigureTitle = "Slots",
            Figure = new RamFigure(channels),
            Notes = notes,
            Groups = new() { new("Configuração", config), new("Velocidade", speed) },
            Table = table,
            Footnote = "Timings (CL, tRCD, tRP) não são expostos pelo Windows: veja na BIOS.",
        };
    }

    // ---------------- Placa-mãe e BIOS ----------------

    private static Sheet BoardSheet(SystemInventory inv, DateTime now)
    {
        if (inv.Board is not { } b) return Missing(Board, "Placa-mãe e BIOS", inv.Errors.GetValueOrDefault(SystemInventory.BoardTitle));
        var f = inv.Firmware;
        var board = new List<SpecRow> { new("Fabricante", b.Maker), new("Modelo", b.Model), new("Revisão", b.Revision), new("Nº de série", b.Serial) };
        var groups = new List<SpecGroup> { new("Placa-mãe", board) };
        if (f != null)
        {
            var bios = new List<SpecRow> { new("Fabricante", f.Vendor), new("Versão", f.Version), new("Data", Fmt.Date(f.Date), Fmt.Ago(f.Date, now)) };
            if (f.Smbios != null) bios.Add(new("SMBIOS", f.Smbios, null, "Versão da tabela de hardware que a BIOS entrega ao Windows."));
            groups.Add(new("BIOS", bios));
        }
        if (b.SystemMaker != null || b.SystemModel != null)
            groups.Add(new("Computador", new() { new("Fabricante", b.SystemMaker ?? "—"), new("Modelo", b.SystemModel ?? "—") }));

        ChainFigure? chain = null;
        var notes = new List<NoteItem>();
        if (f != null)
        {
            chain = new ChainFigure(new()
            {
                new(f.Uefi, "Firmware UEFI", f.Uefi == false ? "A placa inicia no modo legado (CSM). O Windows 11 pede UEFI." : "A placa inicia em modo UEFI, não no modo legado (CSM).",
                    f.Uefi switch { true => "UEFI", false => "Legado", _ => "—" }),
                new(f.SecureBoot, "Secure Boot", "Só carrega sistemas e drivers assinados na inicialização.", Fmt.OnOff(f.SecureBoot)),
                new(f.Tpm == null ? null : f.Tpm.StartsWith("2") && f.TpmReady != false, "TPM",
                    "Chip de segurança usado pelo BitLocker e pelo Windows Hello.",
                    f.Tpm == null ? "—" : f.TpmReady == false && f.Tpm != "Ausente" ? $"{f.Tpm} · desligado" : f.Tpm, Last: true),
            });
            notes.Add(f.Windows11Ready switch
            {
                true => new(Tone.Ok, "Requisitos de segurança do Windows 11 atendidos.", ""),
                false => new(Tone.Warn, "Falta um requisito do Windows 11:", "UEFI, Secure Boot e TPM 2.0 ligados. Ajuste na BIOS."),
                _ => new(Tone.None, "Parte da segurança não pôde ser lida.", "O TPM só é lido com o CoreBi como administrador."),
            });
        }

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
            FigureTitle = chain != null ? "Inicialização segura" : null,
            Figure = chain,
            Notes = notes,
            Groups = groups,
        };
    }

    // ---------------- Armazenamento ----------------

    private static Sheet StorageSheet(SystemInventory inv, IReadOnlyList<DriveCard> drives)
    {
        var disks = new List<DiskView>();
        foreach (var d in inv.Disks)
        {
            var vols = drives.Where(v => v.Disk.Length > 0 && v.Disk == d.Model).ToList();
            double size = Math.Max(1, d.Size);
            var volumes = vols.Select(v => new VolumeView(v.Letter, string.IsNullOrWhiteSpace(v.Label) ? v.Kind : v.Label,
                $"{DriveCard.Fmt(v.Free)} livres de {DriveCard.Fmt(v.Total)}", Math.Max(0.04, v.Total / size), v.UsedFraction,
                $"{v.Letter} · {v.FileSystem} · {v.UsedPercent} usados")).ToList();
            double free = vols.Sum(v => (double)v.Free);
            var (sv, su) = Split(Fmt.Bytes(d.Size));
            var chips = new List<string> { d.Bus, d.Media, d.Removable && d.Bus != "USB" ? "removível" : "" }.Where(x => x.Length > 0 && x != "—").ToList();
            disks.Add(new DiskView(d.Model, string.Join(" · ", new[] { $"Disco {d.Index}", d.Firmware != "—" ? $"firmware {d.Firmware}" : null }.Where(x => x != null)),
                chips, sv, su, vols.Count > 0 ? $"{DriveCard.Fmt((long)free)} livres" : "sem unidade montada", volumes));
        }
        double total = inv.Disks.Sum(d => (double)d.Size);
        double freeAll = drives.Sum(v => (double)v.Free), usedAll = drives.Sum(v => (double)(v.Total - v.Free));
        var (tv, tu) = Split(Fmt.Bytes(total));
        var (uv, uu) = Split(Fmt.Bytes(usedAll));
        var (fv, fu) = Split(Fmt.Bytes(freeAll));
        return new Sheet
        {
            Key = Storage,
            Eyebrow = $"{inv.Disks.Count} {(inv.Disks.Count == 1 ? "disco" : "discos")} · {drives.Count} {(drives.Count == 1 ? "unidade" : "unidades")}",
            Title = total > 0 ? Fmt.Bytes(total) : "—",
            Subtitle = drives.Count > 0 ? $"{Fmt.Bytes(freeAll)} livres no total" : "Nenhuma unidade montada",
            Readouts = new()
            {
                new("Em uso", uv, uu),
                new("Livre", fv, fu),
                new("Discos", inv.Disks.Count.ToString(Br)),
                new("Unidades", drives.Count.ToString(Br)),
            },
            Disks = disks,
            Link = new("Saúde, temperatura e vida útil em Discos", "PageDisks"),
        };
    }

    // ---------------- Sistema operacional ----------------

    private static Sheet OsSheet(SystemInventory inv, DateTime now)
    {
        if (inv.Os is not { } o) return Missing(Os, "Sistema operacional", inv.Errors.GetValueOrDefault(SystemInventory.OsTitle));
        string up = o.LastBoot is DateTime boot ? Fmt.Span(now - boot) : "—";
        var (upValue, upUnit) = SplitSpan(up);
        var windows = new List<SpecRow>
        {
            new("Edição", o.Name), new("Versão", o.Version), new("Build", o.Build), new("Arquitetura", o.Architecture), new("Idioma", Cap(o.Language)),
        };
        var computer = new List<SpecRow> { new("Nome", o.Machine), new("Usuário", o.User), new(o.Domain ? "Domínio" : "Grupo de trabalho", o.Workgroup) };
        var session = new List<SpecRow> { new("Instalado em", Fmt.Date(o.Installed), Fmt.Ago(o.Installed, now)), new("Último boot", Fmt.Stamp(o.LastBoot)), new("Ligado há", up) };
        if (o.LastUpdate != null) session.Insert(1, new("Última atualização", Fmt.Date(o.LastUpdate), o.LastUpdateId));

        var events = new List<TimelineEvent>();
        if (o.Installed != null) events.Add(new("Instalado", Fmt.Date(o.Installed), Fmt.Ago(o.Installed, now), false));
        if (o.LastUpdate != null) events.Add(new("Última atualização", Fmt.Date(o.LastUpdate), o.LastUpdateId ?? "", false));
        if (o.LastBoot != null) events.Add(new("Ligado", Fmt.Stamp(o.LastBoot), "", false));
        events.Add(new("Agora", now.ToString("HH:mm", CultureInfo.InvariantCulture), o.LastBoot != null ? $"ligado há {up}" : "", true));

        string arch = o.Architecture.StartsWith("64") ? "64" : o.Architecture.StartsWith("32") ? "32" : "—";
        return new Sheet
        {
            Key = Os,
            Eyebrow = "Microsoft",
            Title = o.Name,
            Subtitle = $"Versão {o.Version} · build {o.Build} · {o.Architecture}",
            Readouts = new()
            {
                new("Versão", o.Version),
                new("Build", o.Build),
                new("Arquitetura", arch, arch != "—" ? "bits" : ""),
                new("Ligado há", upValue, upUnit),
            },
            FigureTitle = "Linha do tempo",
            Figure = new TimelineFigure(events),
            Groups = new() { new("Windows", windows), new("Computador", computer), new("Sessão", session) },
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
