using LibreHardwareMonitor.Hardware;

using System.Text.RegularExpressions;

namespace CoreBi.Engine.Monitoring.Sensors;

/// <summary>
/// Nomes da biblioteca de sensores em português claro, por componente e tipo ("CPU Core Max" → "Núcleo mais exigido").
/// Nome desconhecido fica como veio: melhor o termo técnico do que um palpite errado.
/// </summary>
public static class SensorNaming
{
    private enum Part { Cpu, Gpu, Memory, Board, Storage, Network, Other }

    private static Part PartOf(HardwareType t) => t switch
    {
        HardwareType.Cpu => Part.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => Part.Gpu,
        HardwareType.Memory => Part.Memory,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => Part.Board,
        HardwareType.Storage => Part.Storage,
        HardwareType.Network => Part.Network,
        _ => Part.Other
    };

    // Nomes exatos que dependem do tipo do sensor (o mesmo "GPU Core" é temperatura, clock e uso)
    private static readonly Dictionary<(SensorType, string), string> ByType = new()
    {
        [(SensorType.Temperature, "Package")] = "Encapsulamento",
        [(SensorType.Temperature, "CPU Package")] = "Encapsulamento",
        [(SensorType.Temperature, "Core Max")] = "Núcleo mais quente",
        [(SensorType.Temperature, "Core Average")] = "Média dos núcleos",
        [(SensorType.Temperature, "CPU")] = "Processador",
        [(SensorType.Temperature, "Temperature")] = "Temperatura",
        [(SensorType.Load, "Core Max")] = "Núcleo mais exigido",
        [(SensorType.Power, "Package")] = "Total do processador",
        [(SensorType.Power, "CPU Package")] = "Total do processador",
        [(SensorType.Load, "Memory")] = "Memória em uso",
        [(SensorType.Load, "Virtual Memory")] = "Memória em uso",
        [(SensorType.Load, "GPU Memory")] = "Memória (VRAM)",
        [(SensorType.Clock, "GPU Memory")] = "Memória (VRAM)",
        [(SensorType.Voltage, "CPU Core")] = "Núcleo do processador",
        [(SensorType.Voltage, "Vcore")] = "Núcleo do processador",
        [(SensorType.Voltage, "CPU")] = "Núcleo do processador",
    };

    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        // Processador
        ["CPU Total"] = "Total",
        ["CPU Core Max"] = "Núcleo mais exigido",
        ["Core (Tctl/Tdie)"] = "Processador (Tctl/Tdie)",
        ["Core (Tctl)"] = "Processador (Tctl)",
        ["Core (Tdie)"] = "Processador (Tdie)",
        ["CCDs Max (Tdie)"] = "Chiplet mais quente",
        ["CCDs Average (Tdie)"] = "Média dos chiplets",
        ["Core (SVI2 TFN)"] = "Núcleos (SVI2)",
        ["SoC (SVI2 TFN)"] = "SoC (SVI2)",
        ["Core (SVI3 TFN)"] = "Núcleos (SVI3)",
        ["SoC (SVI3 TFN)"] = "SoC (SVI3)",
        ["Cores"] = "Núcleos",
        ["CPU Cores"] = "Núcleos",
        ["CPU Graphics"] = "Gráfico integrado",
        ["CPU Memory"] = "Controlador de memória",
        ["CPU Platform"] = "Plataforma",
        ["Misc"] = "Outros",
        ["Bus Speed"] = "Barramento",
        ["Core Max"] = "Núcleo mais alto",
        ["Core Average"] = "Média dos núcleos",
        // Placa de vídeo
        ["GPU Core"] = "Núcleo",
        ["GPU Hot Spot"] = "Ponto mais quente",
        ["GPU Memory Junction"] = "Memória (junção)",
        ["GPU Memory Controller"] = "Controlador de memória",
        ["GPU Video Engine"] = "Motor de vídeo",
        ["GPU Bus"] = "Barramento PCIe",
        ["GPU Shader"] = "Shader",
        ["GPU Power"] = "Total da placa",
        ["GPU Package"] = "Total da placa",
        ["GPU Board Power"] = "Total da placa",
        ["GPU SoC"] = "SoC",
        ["GPU Memory Total"] = "VRAM total",
        ["GPU Memory Used"] = "VRAM em uso",
        ["GPU Memory Free"] = "VRAM livre",
        ["D3D Dedicated Memory Used"] = "VRAM em uso (Windows)",
        ["D3D Dedicated Memory Free"] = "VRAM livre (Windows)",
        ["D3D Dedicated Memory Total"] = "VRAM total (Windows)",
        ["D3D Shared Memory Used"] = "Memória compartilhada em uso",
        ["D3D Shared Memory Free"] = "Memória compartilhada livre",
        ["D3D Shared Memory Total"] = "Memória compartilhada total",
        ["GPU PCIe Rx"] = "PCIe recebendo",
        ["GPU PCIe Tx"] = "PCIe enviando",
        ["GPU Fan"] = "Ventoinha",
        // Memória
        ["Memory Used"] = "Em uso",
        ["Memory Available"] = "Disponível",
        ["Virtual Memory Used"] = "Em uso",
        ["Virtual Memory Available"] = "Disponível",
        // Placa-mãe
        ["System"] = "Sistema",
        ["Motherboard"] = "Placa-mãe",
        ["VRM MOS"] = "VRM",
        ["VRM"] = "VRM",
        ["PCH"] = "Chipset (PCH)",
        ["Chipset"] = "Chipset",
        ["CPU Fan"] = "Ventoinha do processador",
        ["CPU Optional Fan"] = "Ventoinha opcional do processador",
        ["Pump"] = "Bomba",
        ["AIO Pump"] = "Bomba do water cooler",
        ["VBat"] = "Bateria do CMOS",
        ["VBAT"] = "Bateria do CMOS",
        ["3VCC"] = "+3,3 V",
        ["+3.3V"] = "+3,3 V",
        ["3VSB"] = "+3,3 V standby",
        ["+3V Standby"] = "+3,3 V standby",
        ["+3.3V Standby"] = "+3,3 V standby",
        ["+5V"] = "+5 V",
        ["+12V"] = "+12 V",
        ["AVCC"] = "AVCC (analógico)",
        ["AVSB"] = "AVSB (analógico, standby)",
        ["VTT"] = "VTT (terminação)",
        ["DRAM"] = "Memória (DRAM)",
        ["CPU VDDP"] = "Processador · VDDP",
        ["CPU SoC"] = "Processador · SoC",
        ["CPU VCORE"] = "Núcleo do processador",
        // Armazenamento
        ["Used Space"] = "Espaço usado",
        ["Read Activity"] = "Atividade de leitura",
        ["Write Activity"] = "Atividade de escrita",
        ["Total Activity"] = "Atividade total",
        ["Data Read"] = "Total lido",
        ["Data Written"] = "Total gravado",
        ["Host Reads"] = "Total lido",
        ["Host Writes"] = "Total gravado",
        ["Read Rate"] = "Leitura",
        ["Write Rate"] = "Escrita",
        ["Available Spare"] = "Reserva disponível",
        ["Available Spare Threshold"] = "Limite da reserva",
        ["Percentage Used"] = "Desgaste",
        ["Remaining Life"] = "Vida restante",
        ["Composite Temperature"] = "Temperatura",
        // Rede
        ["Network Utilization"] = "Utilização",
        ["Data Uploaded"] = "Total enviado",
        ["Data Downloaded"] = "Total recebido",
        ["Upload Speed"] = "Upload",
        ["Download Speed"] = "Download",
    };

    private static readonly (Regex Pattern, string Replacement)[] Patterns =
    {
        (new Regex(@"^(?:CPU )?Core #(\d+) Thread #(\d+)$"), "Núcleo $1 · thread $2"),
        (new Regex(@"^(?:CPU )?Core #(\d+) Distance to TjMax$"), "Núcleo $1 · margem até o limite"),
        (new Regex(@"^(?:CPU )?Core #(\d+) VID$"), "Núcleo $1 · VID"),
        (new Regex(@"^(?:CPU )?Core #(\d+) \(SMU\)$"), "Núcleo $1"),
        (new Regex(@"^(?:CPU )?Core #(\d+)$"), "Núcleo $1"),
        (new Regex(@"^CCD ?(\d+) \(Tdie\)$"), "Chiplet $1 (CCD$1)"),
        (new Regex(@"^GPU Fan (\d+)$"), "Ventoinha $1"),
        (new Regex(@"^(?:Chassis|System) Fan #?(\d+)$"), "Gabinete $1"),
        (new Regex(@"^Fan #?(\d+)$"), "Ventoinha $1"),
        (new Regex(@"^Temperature #?(\d+)$"), "Sensor $1"),
        (new Regex(@"^D3D (.+)$"), "$1"),
    };

    private static readonly Dictionary<string, string> D3D = new(StringComparer.Ordinal)
    {
        ["3D"] = "3D",
        ["Copy"] = "Cópia",
        ["Video Decode"] = "Decodificação de vídeo",
        ["Video Encode"] = "Codificação de vídeo",
        ["Video Processing"] = "Processamento de vídeo",
        ["Compute_0"] = "Computação",
        ["Compute_1"] = "Computação 2",
        ["Graphics_1"] = "Gráficos 2",
        ["Overlay"] = "Sobreposição",
    };

    public static string Label(HardwareType hardware, SensorType type, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        if (ByType.TryGetValue((type, name), out string? typed)) return typed;
        if (Exact.TryGetValue(name, out string? exact)) return exact;
        foreach (var (pattern, replacement) in Patterns)
        {
            var m = pattern.Match(name);
            if (!m.Success) continue;
            if (name.StartsWith("D3D ", StringComparison.Ordinal))
                return D3D.TryGetValue(m.Groups[1].Value, out string? d) ? d : m.Groups[1].Value;
            return m.Result(replacement);
        }
        // Prefixo repetido do componente: dentro do cartão da placa de vídeo, "GPU Memory Used" já é da GPU
        var part = PartOf(hardware);
        if (part == Part.Gpu && name.StartsWith("GPU ", StringComparison.Ordinal)) return name[4..];
        if (part == Part.Cpu && name.StartsWith("CPU ", StringComparison.Ordinal)) return name[4..];
        return name;
    }

    /// <summary>Grupo de tipo dentro de um componente: título no plural e ordem (temperatura primeiro, como no HWMonitor moderno).</summary>
    public static (string Key, string Title, int Order) Group(SensorType t) => t switch
    {
        SensorType.Temperature => ("temp", "Temperaturas", 0),
        SensorType.Load => ("load", "Uso", 1),
        SensorType.Clock => ("clock", "Clocks", 2),
        SensorType.Power => ("power", "Consumo", 3),
        SensorType.Fan => ("fan", "Ventoinhas", 4),
        SensorType.Control => ("control", "Controle das ventoinhas", 5),
        SensorType.Voltage => ("volt", "Tensões", 6),
        SensorType.Current => ("current", "Correntes", 7),
        SensorType.Throughput => ("rate", "Transferência", 8),
        SensorType.Data or SensorType.SmallData => ("data", "Dados", 9),
        SensorType.Level => ("level", "Níveis", 10),
        SensorType.Frequency => ("freq", "Frequências", 11),
        SensorType.Energy => ("energy", "Energia", 12),
        SensorType.Flow => ("flow", "Fluxo", 13),
        SensorType.Humidity => ("humidity", "Umidade", 14),
        _ => ("other", "Outros", 15)
    };

    /// <summary>
    /// Filtro da página, mais largo que o grupo: ventoinhas e o controle delas andam juntos, tensão e corrente também,
    /// e o que é volume de dados (memória, transferência, níveis do disco) fica num filtro só.
    /// </summary>
    public static (string Key, string Title, int Order) Filter(SensorType t) => t switch
    {
        SensorType.Temperature => ("temp", "Temperaturas", 0),
        SensorType.Load => ("load", "Uso", 1),
        SensorType.Clock => ("clock", "Clocks", 2),
        SensorType.Power or SensorType.Energy => ("power", "Consumo", 3),
        SensorType.Fan or SensorType.Control => ("fan", "Ventoinhas", 4),
        SensorType.Voltage or SensorType.Current => ("volt", "Tensões e correntes", 5),
        SensorType.Throughput or SensorType.Data or SensorType.SmallData or SensorType.Level => ("data", "Dados", 6),
        _ => ("other", "Outros", 7)
    };
}
