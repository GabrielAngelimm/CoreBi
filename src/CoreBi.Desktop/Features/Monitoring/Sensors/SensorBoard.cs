using CoreBi.Engine.Monitoring.Sensors;
using LibreHardwareMonitor.Hardware;

namespace CoreBi.Desktop.Features.Monitoring.Sensors;

/// <summary>
/// Grupo de um tipo dentro do componente (Temperaturas, Clocks...), com as linhas na ordem da biblioteca. Tone é o canal do
/// componente (chave da paleta: Cpu, Gpu, Memory, Virtual, Storage, Board, Net ou Steel), usado no filete do título.
/// Unit é a unidade de todas as linhas (o grupo nunca mistura unidades), dita uma vez ao lado do título, porque mínimo, média
/// e máximo vêm sem ela. Continued marca a segunda metade de um grupo longo, posta na coluna da direita: sem título, com as
/// linhas na mesma altura das da esquerda.
/// </summary>
public sealed record SensorSection(string Key, string Title, IReadOnlyList<SensorRow> Rows, string Tone = "Steel")
{
    public string Unit { get; init; } = "";
    public bool Continued { get; init; }
    /// <summary>
    /// "em °C", "em %"; vazio sem unidade e no multiplicador (×), que o próprio número já diz. Clock é termo técnico: o grupo
    /// diz o que ele é ("velocidade em MHz").
    /// </summary>
    public string UnitText => Unit is "" or "×" ? "" : (Key == "clock" ? "velocidade em " : "em ") + Unit;
}

/// <summary>Item do índice: um componente, ou a visão geral (Id nulo).</summary>
public sealed record IndexEntry(string? Id, string Name, string Kind, string Glyph, string? Note, string Tone = "Steel");

/// <summary>Filtro por tipo de sensor, com quantos sensores ele tem no escopo atual.</summary>
public sealed record SensorFilter(string Key, string Title, int Count);

/// <summary>Tudo que a página mostra para um filtro: o índice, os cartões visíveis e os tipos disponíveis.</summary>
public sealed record SensorLayout(IReadOnlyList<IndexEntry> Index, IReadOnlyList<SensorComponent> Cards, IReadOnlyList<SensorFilter> Filters, string? SelectedId, int Total);

/// <summary>Um componente (processador, placa de vídeo, cada disco...) com os sensores agrupados por tipo.</summary>
public sealed class SensorComponent : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Glyph { get; init; }
    public string Tone { get; init; } = "Steel";
    public string? Note { get; init; }
    public required IReadOnlyList<SensorSection> Sections { get; init; }
    public bool CanCollapse { get; init; } = true;
    public int Count => Sections.Sum(s => s.Rows.Count);
    public string CountText => Count == 1 ? "1 sensor" : $"{Count} sensores";

    private bool _expanded = true;
    public bool Expanded { get => _expanded; set => Set(ref _expanded, value); }
}

public static class SensorBoard
{
    private const string Module = "ram", OverviewGlyph = "\uE71D";
    private const int DimmOrder = 40;

    private sealed record Part(string Key, string Name, string Kind, string Glyph, int Order, string? Note, List<SensorRow> Rows, string Tone = "Steel");

    private static (string Kind, string Glyph, int Order) Describe(HardwareType t) => t switch
    {
        HardwareType.Cpu => ("Processador", "cpu", 0),
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => ("Placa de vídeo", "gpu", 10),
        HardwareType.Storage => ("Armazenamento", "storage", 20),
        HardwareType.Motherboard or HardwareType.SuperIO => ("Placa-mãe", "board", 30),
        HardwareType.EmbeddedController => ("Controlador embarcado", "board", 31),
        HardwareType.Network => ("Rede", "net", 50),
        HardwareType.Cooler => ("Refrigeração", "fan", 60),
        HardwareType.Psu => ("Fonte", "", 61),
        HardwareType.Battery => ("Bateria", "", 62),
        _ => ("Outro", "", 70)
    };

    /// <summary>Canal de cor do componente: o mesmo da Visão geral e da Ficha técnica.</summary>
    private static string ToneOf(HardwareType t) => t switch
    {
        HardwareType.Cpu => "Cpu",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "Gpu",
        HardwareType.Storage => "Storage",
        HardwareType.Memory => "Memory",
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => "Board",
        HardwareType.Network => "Net",
        _ => "Steel"
    };

    private static HardwareType TypeOf(SensorRow r) => r.RootId.Length > 0 ? r.RootType : r.HardwareType;

    /// <summary>
    /// A biblioteca mede a memória física e a virtual ou no mesmo bloco (sensores "Virtual Memory...") ou em dois
    /// ("Total Memory" e "Virtual Memory"). Nenhuma das duas entra em Sensores (Gabriel, 2026-11-21): são o uso que o Windows
    /// faz da memória, não uma peça com sensor, e o uso da RAM já está na Visão geral. Os módulos de RAM ficam.
    /// </summary>
    private static bool IsVirtualMemory(SensorRow r) => TypeOf(r) == HardwareType.Memory
        && (r.Name.StartsWith("Virtual Memory", StringComparison.Ordinal) || r.Component.StartsWith("Virtual Memory", StringComparison.Ordinal));

    public static string KeyOf(SensorRow r) => IsVirtualMemory(r) ? r.ComponentId + "#virtual" : r.ComponentId;

    private static List<Part> Parts(IEnumerable<SensorRow> rows)
    {
        var parts = new List<Part>();
        var dimms = new List<Part>();
        foreach (var group in rows.Where(r => r.HasReading).GroupBy(KeyOf))
        {
            var all = group.ToList();
            var first = all[0];
            var type = TypeOf(first);
            if (type == HardwareType.Memory)
            {
                if (IsVirtualMemory(first) || all.Any(r => r.Name is "Memory" or "Memory Used")) continue;
                dimms.Add(new(group.Key, PrettyName(first.Component), "Memória RAM", Module, DimmOrder, null, all, "Memory"));
                continue;
            }
            var (kind, glyph, order) = Describe(type);
            parts.Add(new(group.Key, PrettyName(first.Component), kind, glyph, order, null, all, ToneOf(type)));
        }
        dimms = dimms.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
        for (int i = 0; i < dimms.Count; i++)
            parts.Add(dimms[i] with { Kind = dimms.Count > 1 ? $"Memória RAM · módulo {i + 1} de {dimms.Count}" : "Memória RAM", Order = DimmOrder + i });
        return parts.OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Componente que um atalho de outra página quer abrir: "cpu" (processador), "ram" (primeiro módulo de RAM; null se os módulos
    /// não têm sensor), "board" (placa-mãe) ou "gpu" / "gpu:&lt;nome&gt;" (placa de vídeo). Null se o PC não tem
    /// esse componente.
    /// </summary>
    public static string? Find(IEnumerable<SensorRow> rows, string target)
    {
        var parts = Parts(rows);
        Part? hit = target switch
        {
            "cpu" => parts.FirstOrDefault(p => p.Order == 0),
            "ram" => parts.FirstOrDefault(p => p.Order >= DimmOrder && p.Order < 50),
            "board" => parts.FirstOrDefault(p => p.Order == 30),
            // "gpu" abre a primeira placa de vídeo; "gpu:<nome>" abre a placa com esse nome (página Placa de vídeo)
            "gpu" => parts.FirstOrDefault(p => p.Order == 10),
            _ when target.StartsWith("gpu:", StringComparison.Ordinal) =>
                parts.FirstOrDefault(p => p.Order == 10 && p.Rows.Any(r => r.Hardware == target[4..] || r.Component == target[4..]))
                ?? parts.FirstOrDefault(p => p.Order == 10),
            _ => null
        };
        return hit?.Key;
    }

    /// <summary>
    /// component: Id do componente escolhido no índice (null = visão geral); group: filtro de tipo; search: texto livre.
    /// Os tipos e as contagens dos filtros valem para o componente escolhido, para nenhum filtro levar a uma tela vazia.
    /// columns: colunas de grupos na tela; com duas, um componente de um grupo só (sem driver, ou com um filtro de tipo) divide
    /// as linhas entre as duas colunas, em vez de deixar a da direita vazia.
    /// </summary>
    public static SensorLayout Build(IEnumerable<SensorRow> rows, string? component, string? group, string search, ISet<string> collapsed, int columns = 1)
    {
        var parts = Parts(rows);
        var index = new List<IndexEntry> { new(null, "Visão geral", "Todos os componentes", OverviewGlyph, null, "Signal") };
        index.AddRange(parts.Select(p => new IndexEntry(p.Key, p.Name, p.Kind, p.Glyph, p.Note, p.Tone)));
        if (component != null && parts.All(p => p.Key != component)) component = null;

        var scope = parts.Where(p => component == null || p.Key == component).ToList();
        var filters = scope.SelectMany(p => p.Rows).GroupBy(r => SensorNaming.Filter(r.Type))
                           .OrderBy(g => g.Key.Order).Select(g => new SensorFilter(g.Key.Key, g.Key.Title, g.Count())).ToList();
        if (group != null && filters.All(f => f.Key != group)) group = null;

        var cards = new List<SensorComponent>();
        foreach (var p in scope)
        {
            var shown = p.Rows.Where(r => (group == null || SensorNaming.Filter(r.Type).Key == group) && Matches(r, p, search)).ToList();
            if (shown.Count == 0) continue;
            cards.Add(new SensorComponent
            {
                Id = p.Key,
                Name = p.Name,
                Kind = p.Kind,
                Glyph = p.Glyph,
                Tone = p.Tone,
                Note = p.Note,
                Sections = Sections(shown, p.Tone, columns),
                CanCollapse = component == null,
                Expanded = component != null || !collapsed.Contains(p.Key)
            });
        }
        return new SensorLayout(index, cards, filters, component, parts.Sum(p => p.Rows.Count));
    }

    /// <summary>
    /// Grupos por tipo e unidade (memória em GB e em MB viram dois grupos "Dados", cada um com a sua unidade). Com duas colunas
    /// e um grupo só de 4 linhas ou mais, a segunda metade das linhas vai para a coluna da direita.
    /// </summary>
    private static List<SensorSection> Sections(List<SensorRow> shown, string tone, int columns)
    {
        var sections = shown.GroupBy(r => (Group: SensorNaming.Group(r.Type), Unit: SensorRow.UnitOf(r.Type))).OrderBy(g => g.Key.Group.Order)
                            .Select(g => new SensorSection(g.Key.Group.Key, g.Key.Group.Title, g.ToList(), tone) { Unit = g.Key.Unit }).ToList();
        if (columns < 2 || sections.Count != 1 || sections[0].Rows.Count < 4) return sections;
        var only = sections[0];
        int half = (only.Rows.Count + 1) / 2;
        return new()
        {
            only with { Rows = only.Rows.Take(half).ToList() },
            only with { Rows = only.Rows.Skip(half).ToList(), Continued = true },
        };
    }

    private static readonly System.Text.RegularExpressions.Regex CoreName = new(@"^(CPU )?Core #\d+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Dica de "Núcleo mais exigido" e "Núcleo mais quente": qual núcleo é, agora. A biblioteca entrega só o maior valor; o
    /// núcleo sai das linhas irmãs (mesmo componente e tipo) com o maior valor neste instante. Null nas outras linhas.
    /// </summary>
    public static string? PeakTip(IEnumerable<SensorRow> rows, SensorRow row)
    {
        if (row.Name is not ("CPU Core Max" or "Core Max")) return null;
        var top = rows.Where(r => !ReferenceEquals(r, row) && r.ComponentId == row.ComponentId && r.Type == row.Type && r.RawValue != null && CoreName.IsMatch(r.Name))
                      .MaxBy(r => r.RawValue!.Value);
        if (top == null) return null;
        return $"Agora: {top.Label} ({top.Value})." + (row.Tip is { } technical ? $"\n{technical}" : "");
    }

    /// <summary>Nome do componente sem marcas registradas e sem espaços sobrando.</summary>
    public static string PrettyName(string name)
    {
        foreach (string mark in new[] { "(R)", "(r)", "(TM)", "(tm)", "®", "™" }) name = name.Replace(mark, "");
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool Matches(SensorRow r, Part p, string search) => search.Length == 0
        || r.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || p.Kind.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || SensorNaming.Group(r.Type).Title.Contains(search, StringComparison.CurrentCultureIgnoreCase);
}
