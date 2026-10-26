using CoreBi.Engine.Monitoring.Sensors;
using LibreHardwareMonitor.Hardware;
using System.ComponentModel;

namespace CoreBi.Desktop.Features.Monitoring.Sensors;

/// <summary>Leitura de destaque no cabeçalho de um componente: rótulo e o sensor ao vivo (número e unidade).</summary>
public sealed record SensorReadout(string Label, SensorRow Row, bool First);

/// <summary>Grupo de um tipo dentro do componente (Temperaturas, Clocks...), com as linhas na ordem da biblioteca.</summary>
public sealed record SensorSection(string Key, string Title, IReadOnlyList<SensorRow> Rows)
{
    public string Unit => Rows.Select(r => r.Unit).Distinct().Count() == 1 ? Rows[0].Unit : "";
}

/// <summary>Um componente (processador, placa de vídeo, cada disco...) com os sensores agrupados por tipo.</summary>
public sealed class SensorComponent : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Nome sem o fabricante, para o índice estreito ("GeForce RTX 4060 Ti").</summary>
    public required string ShortName { get; init; }
    public required string Kind { get; init; }
    public required string Glyph { get; init; }
    public required int Channel { get; init; }
    public required IReadOnlyList<SensorSection> Sections { get; init; }
    public required IReadOnlyList<SensorReadout> Readouts { get; init; }
    public SensorReadout? Lead => Readouts.FirstOrDefault();
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

/// <summary>Monta os componentes da página Sensores a partir das linhas do monitor, com o filtro de tipo e a busca.</summary>
public static class SensorBoard
{
    public static (string Kind, string Glyph, int Order) Describe(HardwareType t) => t switch
    {
        HardwareType.Cpu => ("Processador", "", 0),
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => ("Placa de vídeo", "", 1),
        HardwareType.Memory => ("Memória", "", 2),
        HardwareType.Motherboard or HardwareType.SuperIO => ("Placa-mãe", "", 3),
        HardwareType.EmbeddedController => ("Controlador embarcado", "", 4),
        HardwareType.Cooler => ("Refrigeração", "", 5),
        HardwareType.Psu => ("Fonte", "", 6),
        HardwareType.Storage => ("Armazenamento", "", 7),
        HardwareType.Network => ("Rede", "", 8),
        HardwareType.Battery => ("Bateria", "", 9),
        _ => ("Outro", "", 10)
    };

    private static HardwareType TypeOf(SensorRow r) => r.RootId.Length > 0 ? r.RootType : r.HardwareType;

    public static List<SensorComponent> Build(IEnumerable<SensorRow> rows, string? group, string search, ISet<string> collapsed)
    {
        var list = new List<(SensorComponent C, int Order, bool Quiet)>();
        foreach (var hw in rows.GroupBy(r => r.ComponentId))
        {
            var all = hw.ToList();
            var first = all[0];
            var (kind, glyph, order) = Describe(TypeOf(first));
            var shown = all.Where(r => r.HasReading && (group == null || SensorNaming.Group(r.Type).Key == group) && Matches(r, search)).ToList();
            if (shown.Count == 0) continue;
            var sections = shown.GroupBy(r => SensorNaming.Group(r.Type))
                                .OrderBy(g => g.Key.Order)
                                .Select(g => new SensorSection(g.Key.Key, g.Key.Title, g.ToList()))
                                .ToList();
            var c = new SensorComponent
            {
                Id = hw.Key,
                Name = PrettyName(TypeOf(first), first.Component),
                ShortName = ShortName(PrettyName(TypeOf(first), first.Component)),
                Kind = kind,
                Glyph = glyph,
                Channel = first.Channel,
                Sections = sections,
                Readouts = Readouts(TypeOf(first), all),
                Expanded = !collapsed.Contains(hw.Key)
            };
            // Adaptadores de rede virtuais e parados descem para o fim da lista
            bool quiet = TypeOf(first) == HardwareType.Network && all.All(r => r.RawValue is null or 0);
            list.Add((c, order, quiet));
        }
        return list.OrderBy(x => x.Quiet).ThenBy(x => x.Order).ThenBy(x => x.C.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => x.C).ToList();
    }

    /// <summary>Nome do componente sem marcas registradas; a memória ganha nome em português.</summary>
    public static string PrettyName(HardwareType type, string name)
    {
        if (type == HardwareType.Memory)
            return name switch { "Total Memory" or "Generic Memory" => "Memória física", "Virtual Memory" => "Memória virtual", _ => name };
        foreach (string mark in new[] { "(R)", "(r)", "(TM)", "(tm)", "®", "™" }) name = name.Replace(mark, "");
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly string[] Vendors = { "NVIDIA", "AMD", "Intel", "ASUS", "ASUSTeK", "ASRock", "MSI", "Gigabyte", "GIGABYTE" };

    public static string ShortName(string name)
    {
        int space = name.IndexOf(' ');
        return space > 0 && Vendors.Contains(name[..space]) ? name[(space + 1)..] : name;
    }

    private static bool Matches(SensorRow r, string search) => search.Length == 0
        || r.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || r.Component.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || r.Hardware.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || SensorNaming.Group(r.Type).Title.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>As grandezas que resumem cada tipo de componente, na ordem em que aparecem no cabeçalho do cartão.</summary>
    private static List<SensorReadout> Readouts(HardwareType type, List<SensorRow> rows)
    {
        var picks = new List<(string Label, SensorRow? Row)>();
        SensorRow? Find(SensorType t, params string[] names) =>
            names.Select(n => rows.FirstOrDefault(r => r.Type == t && r.Name == n)).FirstOrDefault(r => r != null);
        SensorRow? First(SensorType t) => rows.FirstOrDefault(r => r.Type == t && r.RawValue != null);

        switch (type)
        {
            case HardwareType.Cpu:
                picks.Add(("Temperatura", Find(SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Package", "Core (Tctl)", "Core Max") ?? First(SensorType.Temperature)));
                picks.Add(("Uso", Find(SensorType.Load, "CPU Total")));
                picks.Add(("Consumo", Find(SensorType.Power, "Package", "CPU Package")));
                break;
            case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel:
                picks.Add(("Temperatura", Find(SensorType.Temperature, "GPU Core") ?? First(SensorType.Temperature)));
                picks.Add(("Uso", Find(SensorType.Load, "GPU Core", "D3D 3D")));
                picks.Add(("Consumo", Find(SensorType.Power, "GPU Package", "GPU Power", "GPU Board Power")));
                picks.Add(("Clock", Find(SensorType.Clock, "GPU Core")));
                break;
            case HardwareType.Memory:
                picks.Add(("Uso", Find(SensorType.Load, "Memory", "Virtual Memory")));
                picks.Add(("Em uso", Find(SensorType.Data, "Memory Used", "Virtual Memory Used")));
                picks.Add(("Disponível", Find(SensorType.Data, "Memory Available", "Virtual Memory Available")));
                break;
            case HardwareType.Storage:
                picks.Add(("Temperatura", First(SensorType.Temperature)));
                picks.Add(("Espaço usado", Find(SensorType.Load, "Used Space")));
                picks.Add(("Atividade", Find(SensorType.Load, "Total Activity")));
                break;
            case HardwareType.Network:
                picks.Add(("Download", Find(SensorType.Throughput, "Download Speed")));
                picks.Add(("Upload", Find(SensorType.Throughput, "Upload Speed")));
                break;
            default:
                // Placa-mãe e afins: a temperatura mais alta do momento não é um sensor fixo; mostra a do processador e a do sistema
                picks.Add(("Processador", Find(SensorType.Temperature, "CPU", "CPU Core")));
                picks.Add(("Sistema", Find(SensorType.Temperature, "System", "Motherboard") ?? First(SensorType.Temperature)));
                picks.Add(("Ventoinha", rows.FirstOrDefault(r => r.Type == SensorType.Fan && r.RawValue is > 0)));
                break;
        }
        var result = new List<SensorReadout>();
        foreach (var (label, row) in picks)
            if (row != null && result.All(r => r.Row != row))
                result.Add(new SensorReadout(label, row, result.Count == 0));
        return result;
    }
}
