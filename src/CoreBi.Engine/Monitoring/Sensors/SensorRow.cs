using LibreHardwareMonitor.Hardware;

using System.ComponentModel;


namespace CoreBi.Engine.Monitoring.Sensors;

/// <summary>Linha do painel de sensores, com notificação de mudança para a UI.</summary>
public class SensorRow : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Hardware { get; init; } = "";
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public SensorType Type { get; init; }
    public HardwareType HardwareType { get; init; }

    /// <summary>Componente de primeiro nível (o chip Super I/O pertence à placa-mãe). Vazio = o próprio Hardware.</summary>
    public string RootId { get; init; } = "";
    public string RootName { get; init; } = "";
    public HardwareType RootType { get; init; }
    public string Component => RootName.Length > 0 ? RootName : Hardware;
    public string ComponentId => RootId.Length > 0 ? RootId : Hardware;

    private string? _label;
    /// <summary>Nome em português claro ("Core #1" → "Núcleo 1"); Name continua com o nome técnico da biblioteca.</summary>
    public string Label => _label ??= SensorNaming.Label(HardwareType, Type, Name);
    /// <summary>Dica com o nome técnico, quando o rótulo traduzido é diferente.</summary>
    public string? Tip => Label == Name ? null : Name;

    private float? _value, _min, _max;
    private double _sum;
    private long _count;
    public float? RawValue => _value;
    /// <summary>Menor e maior valor vistos desde que os sensores abriram (ou desde ResetStats).</summary>
    public float? RawMin => _min;
    public float? RawMax => _max;
    /// <summary>Já houve alguma leitura válida (sensor que nunca respondeu não aparece na página).</summary>
    public bool HasReading => _count > 0;
    public float? AvgValue => _count > 0 ? (float)(_sum / _count) : null;

    /// <summary>Ordem de exibição por tipo (clocks e temperaturas primeiro, como no GPU-Z).</summary>
    public int Order => Type switch
    {
        SensorType.Clock => 0,
        SensorType.Temperature => 1,
        SensorType.Load => 2,
        SensorType.Fan => 3,
        SensorType.Control => 4,
        SensorType.Power => 5,
        SensorType.Voltage => 6,
        SensorType.Current => 7,
        SensorType.SmallData => 8,
        SensorType.Data => 9,
        SensorType.Throughput => 10,
        _ => 11
    };
    public string Value => Format(_value);
    public string Min => Format(_min);
    public string Max => Format(_max);

    /// <summary>Número sem a unidade, para tabelas que mostram a unidade uma vez só.</summary>
    public string Number => Digits(_value);
    public string Unit => UnitOf(Type);
    public string MinNumber => IsCounter ? "" : Digits(_min);
    public string AvgNumber => IsCounter ? "" : Digits(AvgValue);
    public string MaxNumber => IsCounter ? "" : Digits(_max);

    /// <summary>Contador que só cresce (dados lidos, gravados, baixados): mínimo, média e máximo não dizem nada.</summary>
    public bool IsCounter => Type == SensorType.Energy
        || Type == SensorType.Data && (Name.StartsWith("Data ", StringComparison.Ordinal) || Name.StartsWith("Host ", StringComparison.Ordinal));

    /// <summary>Canal de cor: 0 = processador (verde-água), 1 = placa de vídeo (cobre), 2 = os demais (aço).</summary>
    public int Channel => (RootId.Length > 0 ? RootType : HardwareType) switch
    {
        HardwareType.Cpu => 0,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => 1,
        _ => 2
    };

    /// <summary>Veredito só para temperatura: 0 normal, 1 atenção, 2 crítico (discos esquentam menos).</summary>
    public int Level
    {
        get
        {
            if (Type != SensorType.Temperature || _value is not { } v) return 0;
            bool disk = (RootId.Length > 0 ? RootType : HardwareType) == HardwareType.Storage;
            float warn = disk ? 60 : 85, crit = disk ? 70 : 95;
            return v >= crit ? 2 : v >= warn ? 1 : 0;
        }
    }

    /// <summary>
    /// Nova leitura. Mínimo, média e máximo são contados aqui, desde a abertura ou desde ResetStats; o mínimo e o máximo da
    /// biblioteca só servem de ponto de partida na primeira leitura.
    /// </summary>
    public void Update(float? value, float? min, float? max)
    {
        value = Finite(value); min = Finite(min); max = Finite(max);
        if (value is { } v)
        {
            if (_count == 0) { _min = min is { } m && m < v ? m : v; _max = max is { } x && x > v ? x : v; }
            else { if (_min is not { } lo || v < lo) _min = v; if (_max is not { } hi || v > hi) _max = v; }
            _sum += v; _count++;
        }
        else if (_value == null) return;
        _value = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>Recomeça mínimo, média e máximo a partir da leitura atual.</summary>
    public void ResetStats()
    {
        _min = _max = _value;
        _sum = _value ?? 0;
        _count = _value != null ? 1 : 0;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static float? Finite(float? value) => value.HasValue && float.IsFinite(value.Value) ? value : null;

    private string Format(float? v)
    {
        if (v == null) return "—";
        string unit = Unit;
        return unit.Length == 0 ? Digits(v) : $"{Digits(v)} {unit}";
    }

    /// <summary>Casas decimais fixas por grandeza, para os números de uma coluna alinharem pela vírgula.</summary>
    private string Digits(float? v)
    {
        if (v == null) return "—";
        return Type switch
        {
            SensorType.Temperature => $"{v:0.0}",
            SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => $"{v:0.0}",
            SensorType.Clock => $"{v:0}",
            SensorType.Power => $"{v:0.0}",
            SensorType.Voltage => $"{v:0.000}",
            SensorType.Current => $"{v:0.000}",
            SensorType.Fan => $"{v:0}",
            SensorType.Flow => $"{v:0.0}",
            SensorType.Frequency => $"{v:0.0}",
            SensorType.Data => v >= 1000 ? $"{v:N0}" : $"{v:0.0}",
            SensorType.SmallData => $"{v:0}",
            SensorType.Throughput => $"{v / 1048576f:0.0}",
            SensorType.Energy => $"{v:0}",
            SensorType.Factor => $"{v:0.0}",
            _ => $"{v:0.##}"
        };
    }

    public static string UnitOf(SensorType t) => t switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => "%",
        SensorType.Clock => "MHz",
        SensorType.Power => "W",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Frequency => "Hz",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "MB/s",
        SensorType.Energy => "mWh",
        SensorType.Factor => "×",
        _ => ""
    };

    public static string CategoryName(SensorType t) => t switch
    {
        SensorType.Temperature => "Temperatura",
        SensorType.Load => "Carga",
        SensorType.Clock => "Clock",
        SensorType.Power => "Consumo",
        SensorType.Voltage => "Tensão",
        SensorType.Current => "Corrente",
        SensorType.Fan => "Ventoinhas",
        SensorType.Control => "Controle",
        SensorType.Level => "Nível",
        SensorType.Data or SensorType.SmallData => "Dados",
        SensorType.Throughput => "Taxa",
        SensorType.Frequency => "Frequência",
        SensorType.Energy => "Energia",
        SensorType.Flow => "Fluxo",
        SensorType.Humidity => "Umidade",
        SensorType.Factor => "Fator",
        _ => t.ToString()
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}
