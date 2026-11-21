using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Sensors;

internal readonly record struct SensorTypeDescriptor(
    string Unit,
    string Category,
    string GroupKey,
    string GroupTitle,
    int GroupOrder,
    string FilterKey,
    string FilterTitle,
    int FilterOrder);

internal static class SensorTypeMeta
{
    public static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    public static SensorTypeDescriptor Describe(SensorType type) => type switch
    {
        SensorType.Temperature => new("°C", "Temperatura", "temp", "Temperaturas", 0, "temp", "Temperaturas", 0),
        SensorType.Load => new("%", "Carga", "load", "Uso", 1, "load", "Uso", 1),
        SensorType.Clock => new("MHz", "Clock", "clock", "Clocks", 2, "clock", "Clocks", 2),
        SensorType.Power => new("W", "Consumo", "power", "Consumo", 3, "power", "Consumo", 3),
        SensorType.Fan => new("RPM", "Ventoinhas", "fan", "Ventoinhas", 4, "fan", "Ventoinhas", 4),
        SensorType.Control => new("%", "Controle", "control", "Controle das ventoinhas", 5, "fan", "Ventoinhas", 4),
        SensorType.Voltage => new("V", "Tensão", "volt", "Tensões", 6, "volt", "Tensões e correntes", 5),
        SensorType.Current => new("A", "Corrente", "current", "Correntes", 7, "volt", "Tensões e correntes", 5),
        SensorType.Throughput => new("MB/s", "Taxa", "rate", "Transferência", 8, "data", "Dados", 6),
        SensorType.Data => new("GB", "Dados", "data", "Dados", 9, "data", "Dados", 6),
        SensorType.SmallData => new("MB", "Dados", "data", "Dados", 9, "data", "Dados", 6),
        SensorType.Level => new("%", "Nível", "level", "Níveis", 10, "data", "Dados", 6),
        SensorType.Frequency => new("Hz", "Frequência", "freq", "Frequências", 11, "other", "Outros", 7),
        SensorType.Energy => new("mWh", "Energia", "energy", "Energia", 12, "power", "Consumo", 3),
        SensorType.Flow => new("L/h", "Fluxo", "flow", "Fluxo", 13, "other", "Outros", 7),
        SensorType.Humidity => new("%", "Umidade", "humidity", "Umidade", 14, "other", "Outros", 7),
        SensorType.Factor => new("×", "Fator", "factor", "Multiplicadores", 15, "other", "Outros", 7),
        _ => new("", type.ToString(), "other", "Outros", 16, "other", "Outros", 7)
    };
}
