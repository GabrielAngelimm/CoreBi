using System.ComponentModel;
using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Sensors;

public class SensorRow : INotifyPropertyChanged
{
    // Uma instância só: a página Sensores atualiza centenas de linhas por segundo
    private static readonly PropertyChangedEventArgs AllChanged = new(string.Empty);

    public string Id { get; init; } = "";
    public string Hardware { get; init; } = "";
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public SensorType Type { get; init; }
    public HardwareType HardwareType { get; init; }

    public string RootId { get; init; } = "";
    public string RootName { get; init; } = "";
    public HardwareType RootType { get; init; }
    public string Component => RootName.Length > 0 ? RootName : Hardware;
    public string ComponentId => RootId.Length > 0 ? RootId : Hardware;

    private string? _label;
    public string Label => _label ??= SensorNaming.Label(HardwareType, Type, Name);
    public string? Tip => Label == Name ? null : Name;

    private float? _value, _min, _max;
    private double _sum;
    private long _count;
    public float? RawValue => _value;
    public float? RawMin => _min;
    public float? RawMax => _max;
    public bool HasReading => _count > 0;
    public float? AvgValue => _count > 0 ? (float)(_sum / _count) : null;

    public string Value => Format(_value);
    public string Min => Format(_min);
    public string Max => Format(_max);

    public string Number => Digits(_value);
    public string Unit => UnitOf(Type);
    public string MinNumber => IsCounter ? "" : Digits(_min);
    public string AvgNumber => IsCounter ? "" : Digits(AvgValue);
    public string MaxNumber => IsCounter ? "" : Digits(_max);

    /// <summary>Contador que só cresce (dados lidos, gravados, baixados): mínimo, média e máximo não dizem nada.</summary>
    public bool IsCounter => Type == SensorType.Energy
        || (Type == SensorType.Data && (Name.StartsWith("Data ", StringComparison.Ordinal) || Name.StartsWith("Host ", StringComparison.Ordinal)));

    /// <summary>Canal de cor: 0 = processador, 1 = placa de vídeo, 2 = os demais.</summary>
    public int Channel => (RootId.Length > 0 ? RootType : HardwareType) switch
    {
        HardwareType.Cpu => 0,
        var t when SensorTypeMeta.IsGpu(t) => 1,
        _ => 2
    };

    /// <summary>
    /// Veredito só para temperatura: 0 normal, 1 atenção, 2 crítico, com os limites de <see cref="ThermalLimits"/>.
    /// Os limites que o próprio disco informa ("Temperatura de aviso", "Temperatura crítica") não são leituras e ficam neutros.
    /// </summary>
    public int Level
    {
        get
        {
            if (Type != SensorType.Temperature || _value is not { } v || Name is "Warning Temperature" or "Critical Temperature") return 0;
            double warn, crit;
            if ((RootId.Length > 0 ? RootType : HardwareType) == HardwareType.Storage)
                // A biblioteca identifica o NVMe por "/nvme/…"; NVMe é sempre SSD. SATA não diz o tipo aqui.
                (warn, crit) = ThermalLimits.Disk(isSsd: (RootId.Length > 0 ? RootId : Id).StartsWith("/nvme", StringComparison.Ordinal));
            else if (Name is "GPU Hot Spot" or "GPU Memory Junction")
                (warn, crit) = (ThermalLimits.JunctionWarnC, ThermalLimits.JunctionCritC);
            else
                (warn, crit) = (ThermalLimits.ChipWarnC, ThermalLimits.ChipCritC);
            return v >= crit ? 2 : v >= warn ? 1 : 0;
        }
    }

    /// <summary>Mínimo, média e máximo são contados aqui, desde a abertura ou desde ResetStats; os da biblioteca só servem de ponto de partida na primeira leitura.</summary>
    public void Update(float? value, float? min, float? max)
    {
        value = Finite(value);
        min = Finite(min);
        max = Finite(max);
        if (value is { } v)
        {
            // O mínimo da biblioteca às vezes guarda o 0 da primeira leitura antes de o sensor responder
            // (ex.: SSD a 72 °C com mínimo 0). Temperatura, clock e tensão nunca são 0 reais.
            if (_count == 0)
            {
                _min = min is { } m && m < v && !(m <= 0 && NeverZero) ? m : v;
                _max = max is { } x && x > v ? x : v;
            }
            else
            {
                if (_min is not { } lo || v < lo) _min = v;
                if (_max is not { } hi || v > hi) _max = v;
            }
            _sum += v;
            _count++;
        }
        else if (_value == null) return;
        _value = value;
        PropertyChanged?.Invoke(this, AllChanged);
    }

    public void ResetStats()
    {
        _min = _max = _value;
        _sum = _value ?? 0;
        _count = _value != null ? 1 : 0;
        PropertyChanged?.Invoke(this, AllChanged);
    }

    private bool NeverZero => Type is SensorType.Temperature or SensorType.Clock or SensorType.Voltage;

    private static float? Finite(float? value) => value.HasValue && float.IsFinite(value.Value) ? value : null;

    private string Format(float? v)
    {
        if (v == null) return "—";
        string unit = Unit;
        return unit.Length == 0 ? Digits(v) : $"{Digits(v)} {unit}";
    }

    // Casas decimais fixas por grandeza, para os números de uma coluna alinharem pela vírgula.
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

    public static string UnitOf(SensorType t) => SensorTypeMeta.Describe(t).Unit;
    public static string CategoryName(SensorType t) => SensorTypeMeta.Describe(t).Category;

    public event PropertyChangedEventHandler? PropertyChanged;
}
