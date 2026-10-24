using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Power;

/// <summary>Uma leitura de tensão como a biblioteca de sensores entrega: valor atual e a faixa vista desde que o CoreBi abriu.</summary>
public sealed record VoltageInput(HardwareType HardwareType, string Name, float? Value, float? Min, float? Max);

/// <summary>Trilho da fonte ATX (+12 V, +5 V, +3,3 V e standby), com a tolerância de ±5 % da especificação.</summary>
public sealed record Rail(string Title, double Nominal, double Value, double? Min, double? Max)
{
    public const double Tolerance = 0.05;
    public double Deviation => Value / Nominal - 1;
    public bool InTolerance => Math.Abs(Deviation) <= Tolerance;
}

/// <summary>Tensão de um componente numa escala fixa do tipo (o ponto mostra onde a leitura está na faixa usual). Low marca leitura baixa demais.</summary>
public sealed record VoltageLine(string Title, double Value, double? Min, double? Max, double ScaleLo, double ScaleHi, bool Low = false);

public sealed record VoltageMap(IReadOnlyList<Rail> Rails, IReadOnlyList<VoltageLine> Components, int Total);

/// <summary>
/// Organiza as tensões dos sensores: trilhos da fonte lidos pela placa-mãe e as tensões principais de processador, memória e placa
/// de vídeo. Um trilho fora de ±15 % do nominal não é mostrado como trilho: é canal sem calibração da placa (o nome não confere com o valor).
/// </summary>
public static class VoltageMapper
{
    /// <summary>Abaixo disso a pilha do CMOS (CR2032, 3 V) está fraca: a placa pode perder a hora e as configurações.</summary>
    public const double CmosLow = 2.8;

    public static VoltageMap Map(IEnumerable<VoltageInput> inputs)
    {
        var all = inputs.Where(i => i.Value is > 0 && float.IsFinite(i.Value.Value)).ToList();
        var rails = new Dictionary<string, Rail>();
        VoltageLine? core = null, soc = null, dram = null, gpu = null, cmos = null, boardCore = null;
        var vids = new List<VoltageInput>();

        foreach (var v in all)
        {
            string n = v.Name.ToUpperInvariant().Replace(" ", "").Replace(",", ".");
            double value = v.Value!.Value;
            bool board = v.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO;
            if (board)
            {
                if (n.Contains("CMOS") || n.Contains("VBAT") || n.Contains("BATTERY"))
                    cmos ??= Line("Bateria do CMOS", v, 2.4, 3.4) with { Low = value < CmosLow };
                else if (RailOf(n) is (string title, double nominal) && Math.Abs(value / nominal - 1) <= 0.15)
                    rails.TryAdd(title, new Rail(title, nominal, value, v.Min, v.Max));
                else if (n is "VCORE" or "CPUCORE" or "CPUVCORE")
                    boardCore ??= Line("Processador · núcleo", v, 0.6, 1.6);
                else if (n.Contains("DRAM") || n.Contains("DIMM") || n == "MEMORY")
                    dram ??= Line("Memória (DRAM)", v, 1.0, 1.6);
            }
            else if (v.HardwareType == HardwareType.Cpu)
            {
                if (n.StartsWith("CORE(SVI") || n is "CPUCORE" or "CORE" or "VCORE") core = core == null || n.StartsWith("CORE(SVI") ? Line("Processador · núcleo", v, 0.6, 1.6) : core;
                else if (n.StartsWith("SOC")) soc ??= Line("Processador · SoC", v, 0.6, 1.4);
                else if (n.Contains("VID")) vids.Add(v);
            }
            else if (v.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            {
                if (n.StartsWith("GPUCORE") || n == "CORE") gpu ??= Line("Placa de vídeo · núcleo", v, 0.6, 1.2);
            }
        }
        core ??= boardCore;
        if (core == null && vids.Count > 0)
        {
            // Sem a tensão medida: a maior VID pedida pelos núcleos (o processador pede, o regulador entrega)
            var top = vids.MaxBy(x => x.Value)!;
            core = new VoltageLine("Processador · VID", top.Value!.Value, vids.Min(x => x.Min ?? x.Value), vids.Max(x => x.Max ?? x.Value), 0.6, 1.6);
        }

        string[] order = { "+12 V", "+5 V", "+3,3 V", "+5 V standby", "+3,3 V standby" };
        var railList = order.Where(rails.ContainsKey).Select(k => rails[k]).ToList();
        var lines = new[] { core, soc, dram, gpu, cmos }.Where(l => l != null).Select(l => l!).ToList();
        return new VoltageMap(railList, lines, all.Count);
    }

    private static VoltageLine Line(string title, VoltageInput v, double lo, double hi) => new(title, v.Value!.Value, v.Min, v.Max, lo, hi);

    /// <summary>Nome do canal na placa-mãe → trilho e nominal. Cobre as grafias da biblioteca (+12V, 12V, +3.3V, 3VCC, 5VSB, +3V Standby…).</summary>
    internal static (string Title, double Nominal)? RailOf(string n)
    {
        if (n.StartsWith('-')) return null;
        bool standby = n.Contains("VSB") || n.Contains("STANDBY") || n.Contains("STBY");
        if (standby) return n.Contains('5') && !n.Contains("3.3") ? ("+5 V standby", 5) : n.Contains('3') ? ("+3,3 V standby", 3.3) : null;
        if (n.Contains("12V") || n is "VCC12" or "12VCC") return ("+12 V", 12);
        if (n.Contains("3.3V") || n.Contains("3V3") || n is "3VCC" or "VCC3" or "+3V") return ("+3,3 V", 3.3);
        if (n is "+5V" or "5V" or "5VCC" or "VCC5" or "+5VCC") return ("+5 V", 5);
        return null;
    }
}
