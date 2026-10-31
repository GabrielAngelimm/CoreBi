using CoreBi.Engine.Monitoring.Sensors;
using LibreHardwareMonitor.Hardware;
using System.ComponentModel;

namespace CoreBi.Desktop.Features.Monitoring.Sensors;

/// <summary>
/// Grupo de um tipo dentro do componente (Temperaturas, Clocks...), com as linhas na ordem da biblioteca. Tone é o canal do
/// componente (chave da paleta: Cpu, Gpu, Memory, Storage ou Steel), usado no filete do título.
/// </summary>
public sealed record SensorSection(string Key, string Title, IReadOnlyList<SensorRow> Rows, string Tone = "Steel");

/// <summary>Item do índice: um componente, ou a visão geral (Id nulo).</summary>
public sealed record IndexEntry(string? Id, string Name, string Kind, string Glyph, string? Note);

/// <summary>Filtro por tipo de sensor, com quantos sensores ele tem no escopo atual.</summary>
public sealed record SensorFilter(string Key, string Title, int Count);

/// <summary>Tudo que a página mostra para um filtro: o índice, os cartões visíveis e os tipos disponíveis.</summary>
public sealed record SensorLayout(IReadOnlyList<IndexEntry> Index, IReadOnlyList<SensorComponent> Cards, IReadOnlyList<SensorFilter> Filters, string? SelectedId, int Total);

/// <summary>Um componente (processador, placa de vídeo, cada disco...) com os sensores agrupados por tipo.</summary>
public sealed class SensorComponent : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Glyph { get; init; }
    public required int Channel { get; init; }
    /// <summary>Canal do componente na paleta (Cpu, Gpu, Memory, Storage ou Steel).</summary>
    public string Tone { get; init; } = "Steel";
    public string? Note { get; init; }
    public required IReadOnlyList<SensorSection> Sections { get; init; }
    /// <summary>Com um componente só na tela não há o que recolher: o botão some.</summary>
    public bool CanCollapse { get; init; } = true;
    public int Count => Sections.Sum(s => s.Rows.Count);
    public string CountText => Count == 1 ? "1 sensor" : $"{Count} sensores";

    private bool _expanded = true;
    public bool Expanded
    {
        get => _expanded;
        set { if (_expanded == value) return; _expanded = value; PropertyChanged?.Invoke(this, new(nameof(Expanded))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Monta a página Sensores a partir das linhas do monitor. Os componentes saem na ordem do uso: processador, placa de vídeo,
/// armazenamento, placa-mãe, cada módulo de RAM, rede, e por último a memória física e a virtual (poucas informações).
/// </summary>
public static class SensorBoard
{
    private const string Module = "\uEEA0", PhysicalGlyph = "\uE74C", VirtualGlyph = "\uE7C3", OverviewGlyph = "\uE71D";
    private const int PhysicalOrder = 90, VirtualOrder = 91, DimmOrder = 40;

    private sealed record Part(string Key, string Name, string Kind, string Glyph, int Channel, int Order, string? Note, List<SensorRow> Rows, string Tone = "Steel");

    private static (string Kind, string Glyph, int Order) Describe(HardwareType t) => t switch
    {
        HardwareType.Cpu => ("Processador", "", 0),
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => ("Placa de vídeo", "", 10),
        HardwareType.Storage => ("Armazenamento", "", 20),
        HardwareType.Motherboard or HardwareType.SuperIO => ("Placa-mãe", "", 30),
        HardwareType.EmbeddedController => ("Controlador embarcado", "", 31),
        HardwareType.Network => ("Rede", "", 50),
        HardwareType.Cooler => ("Refrigeração", "", 60),
        HardwareType.Psu => ("Fonte", "", 61),
        HardwareType.Battery => ("Bateria", "", 62),
        _ => ("Outro", "", 70)
    };

    /// <summary>Canal de cor do componente: o mesmo da Visão geral, da barra lateral e do Sistema.</summary>
    private static string ToneOf(HardwareType t) => t switch
    {
        HardwareType.Cpu => "Cpu",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "Gpu",
        HardwareType.Storage => "Storage",
        HardwareType.Memory => "Memory",
        _ => "Steel"
    };

    private static HardwareType TypeOf(SensorRow r) => r.RootId.Length > 0 ? r.RootType : r.HardwareType;

    /// <summary>
    /// A biblioteca mede a memória física e a virtual ou no mesmo bloco (sensores "Virtual Memory...") ou em dois
    /// ("Total Memory" e "Virtual Memory"); nos dois casos viram dois componentes.
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
                if (IsVirtualMemory(first))
                    parts.Add(new(group.Key, "Memória virtual", "Uso do Windows", VirtualGlyph, 2, VirtualOrder,
                        "Memória física somada ao arquivo de paginação, no disco. É o limite que o Windows pode prometer aos programas.", all, "Memory"));
                else if (all.Any(r => r.Name is "Memory" or "Memory Used"))
                    parts.Add(new(group.Key, "Memória física", "Uso do Windows", PhysicalGlyph, 2, PhysicalOrder,
                        "A RAM instalada vista pelo Windows: quanto está em uso e quanto está livre agora.", all, "Memory"));
                else
                    dimms.Add(new(group.Key, PrettyName(first.Component), "Memória RAM", Module, 2, DimmOrder, null, all, "Memory"));
                continue;
            }
            var (kind, glyph, order) = Describe(type);
            parts.Add(new(group.Key, PrettyName(first.Component), kind, glyph, first.Channel, order, null, all, ToneOf(type)));
        }
        dimms = dimms.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
        for (int i = 0; i < dimms.Count; i++)
            parts.Add(dimms[i] with { Kind = dimms.Count > 1 ? $"Memória RAM · módulo {i + 1} de {dimms.Count}" : "Memória RAM", Order = DimmOrder + i });
        return parts.OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Componente que um atalho de outra página quer abrir: "cpu" (processador), "ram" (primeiro módulo de RAM, ou a memória
    /// física se os módulos não têm sensor) ou "board" (placa-mãe). Null se o PC não tem esse componente.
    /// </summary>
    public static string? Find(IEnumerable<SensorRow> rows, string target)
    {
        var parts = Parts(rows);
        Part? hit = target switch
        {
            "cpu" => parts.FirstOrDefault(p => p.Order == 0),
            "ram" => parts.FirstOrDefault(p => p.Order >= DimmOrder && p.Order < 50) ?? parts.FirstOrDefault(p => p.Order == PhysicalOrder),
            "board" => parts.FirstOrDefault(p => p.Order == 30),
            _ => null
        };
        return hit?.Key;
    }

    /// <summary>
    /// component: Id do componente escolhido no índice (null = visão geral); group: filtro de tipo; search: texto livre.
    /// Os tipos e as contagens dos filtros valem para o componente escolhido, para nenhum filtro levar a uma tela vazia.
    /// </summary>
    public static SensorLayout Build(IEnumerable<SensorRow> rows, string? component, string? group, string search, ISet<string> collapsed)
    {
        var parts = Parts(rows);
        var index = new List<IndexEntry> { new(null, "Visão geral", "Todos os componentes", OverviewGlyph, null) };
        index.AddRange(parts.Select(p => new IndexEntry(p.Key, p.Name, p.Kind, p.Glyph, p.Note)));
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
                Channel = p.Channel,
                Tone = p.Tone,
                Note = p.Note,
                Sections = shown.GroupBy(r => SensorNaming.Group(r.Type)).OrderBy(g => g.Key.Order)
                                .Select(g => new SensorSection(g.Key.Key, g.Key.Title, g.ToList(), p.Tone)).ToList(),
                CanCollapse = component == null,
                Expanded = component != null || !collapsed.Contains(p.Key)
            });
        }
        return new SensorLayout(index, cards, filters, component, parts.Sum(p => p.Rows.Count));
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
