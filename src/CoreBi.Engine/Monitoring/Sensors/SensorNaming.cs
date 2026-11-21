using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Sensors;

/// <summary>
/// Nomes da biblioteca de sensores em português claro, por componente e tipo ("CPU Core Max" → "Núcleo mais exigido").
/// Nome desconhecido fica como veio: melhor o termo técnico do que um palpite errado.
/// </summary>
public static class SensorNaming
{
    // Nomes exatos que dependem do tipo do sensor (o mesmo "GPU Core" é temperatura, clock e uso).
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
        ["Cores (Average)"] = "Núcleos · média",
        ["Cores (Average Effective)"] = "Núcleos · média efetiva",
        ["Cores (Max)"] = "Núcleos · máximo",
        ["Core Max"] = "Núcleo mais alto",
        ["Core Average"] = "Média dos núcleos",
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
        ["Memory Used"] = "Em uso",
        ["Memory Available"] = "Disponível",
        ["Virtual Memory Used"] = "Em uso",
        ["Virtual Memory Available"] = "Disponível",
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
        ["Warning Temperature"] = "Temperatura de aviso",
        ["Critical Temperature"] = "Temperatura crítica",
        ["Free Space"] = "Espaço livre",
        ["Total Space"] = "Espaço total",
        ["Life"] = "Vida útil",
        ["Network Utilization"] = "Utilização",
        ["Data Uploaded"] = "Total enviado",
        ["Data Downloaded"] = "Total recebido",
        ["Upload Speed"] = "Upload",
        ["Download Speed"] = "Download",
    };

    private static readonly (Regex Pattern, string Replacement)[] Patterns =
    {
        (new Regex(@"^(?:CPU )?Core #(\d+) Thread #(\d+)$", RegexOptions.Compiled), "Núcleo $1 · thread $2"),
        (new Regex(@"^(?:CPU )?Core #(\d+) Distance to TjMax$", RegexOptions.Compiled), "Núcleo $1 · margem até o limite"),
        (new Regex(@"^(?:CPU )?Core #(\d+) VID$", RegexOptions.Compiled), "Núcleo $1 · VID"),
        (new Regex(@"^(?:CPU )?Core #(\d+) \(SMU\)$", RegexOptions.Compiled), "Núcleo $1"),
        (new Regex(@"^(?:CPU )?Core #(\d+) \(Effective\)$", RegexOptions.Compiled), "Núcleo $1 · efetivo"),
        (new Regex(@"^(?:CPU )?Core #(\d+)$", RegexOptions.Compiled), "Núcleo $1"),
        (new Regex(@"^CCD ?(\d+) \(Tdie\)$", RegexOptions.Compiled), "Chiplet $1"),
        (new Regex(@"^GPU Fan (\d+)$", RegexOptions.Compiled), "Ventoinha $1"),
        (new Regex(@"^(?:Chassis|System) Fan #?(\d+)$", RegexOptions.Compiled), "Gabinete $1"),
        (new Regex(@"^Fan #?(\d+)$", RegexOptions.Compiled), "Ventoinha $1"),
        (new Regex(@"^Temperature #?(\d+)$", RegexOptions.Compiled), "Sensor $1"),
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
        if (name.StartsWith("D3D ", StringComparison.Ordinal))
        {
            string rest = name[4..];
            return D3D.TryGetValue(rest, out string? d) ? d : rest;
        }
        foreach (var (pattern, replacement) in Patterns)
        {
            var m = pattern.Match(name);
            if (m.Success) return m.Result(replacement);
        }
        // Prefixo repetido do componente: dentro do cartão da placa de vídeo, "GPU Memory Used" já é da GPU.
        if (SensorTypeMeta.IsGpu(hardware) && name.StartsWith("GPU ", StringComparison.Ordinal)) return name[4..];
        if (hardware == HardwareType.Cpu && name.StartsWith("CPU ", StringComparison.Ordinal)) return name[4..];
        return name;
    }

    public static (string Key, string Title, int Order) Group(SensorType t)
    {
        var d = SensorTypeMeta.Describe(t);
        return (d.GroupKey, d.GroupTitle, d.GroupOrder);
    }

    /// <summary>
    /// Filtro da página, mais largo que o grupo: ventoinhas e o controle delas andam juntos, tensão e corrente também,
    /// e o que é volume de dados (memória, transferência, níveis do disco) fica num filtro só.
    /// </summary>
    public static (string Key, string Title, int Order) Filter(SensorType t)
    {
        var d = SensorTypeMeta.Describe(t);
        return (d.FilterKey, d.FilterTitle, d.FilterOrder);
    }
}
