using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Inventory;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

public static partial class SystemSheets
{
    private static Sheet CpuSheet(SystemInventory inv)
    {
        if (inv.Cpus.FirstOrDefault() is not { } c) return Missing(Cpu, "Processador", inv.Errors.GetValueOrDefault(SystemInventory.CpuTitle));
        var t = c.Topology;
        int cores = t.Cores.Count > 0 ? t.Cores.Count : c.Cores, threads = t.Threads > 0 ? t.Threads : c.Threads;
        if (inv.Cpus.Count > 1) { cores = inv.Cpus.Sum(x => x.Cores); threads = inv.Cpus.Sum(x => x.Threads); }
        var l3 = t.Caches.Where(x => x.Level == 3).ToList();
        var (l3Value, l3Unit) = l3.Count > 0 ? Split(Fmt.Cache(l3.Sum(x => x.Size * x.Instances))) : c.L3Kb > 0 ? Split(Fmt.Cache(c.L3Kb * 1024)) : ("—", "");
        bool smt = threads > cores;
        string amd = c.Vendor == "AMD" ? "SVM" : "VT-x";

        // A placa resume; a identificação repete o nome completo, como na etiqueta do processador.
        var id = new List<SpecRow>
        {
            new("Fabricante", c.Vendor),
            new("Nome completo", c.Name),
        };
        if (c.Family != null) id.Add(new("Família · modelo · stepping", $"{c.Family} · {c.Model} · {c.Stepping}", null,
            $"Em hexadecimal: família {c.Family:X}h, modelo {c.Model:X}h. É o que identifica a geração exata do chip."));
        id.Add(new("Arquitetura", c.Architecture, c.Architecture switch { "x64" => "64 bits", "ARM64" => "ARM, 64 bits", "x86" => "32 bits", _ => null }));
        id.Add(new("Soquete", c.Socket, Help: "O encaixe na placa-mãe. Define quais processadores ela aceita numa troca."));
        if (c.Microcode != null) id.Add(new("Microcódigo", c.Microcode, null, "Revisão das correções internas do processador, carregada pela BIOS ou pelo Windows."));
        if (inv.Cpus.Count > 1) id.Add(new("Processadores", inv.Cpus.Count.ToString(Br)));

        // Núcleos, threads, clock base e L3 estão na placa logo acima: o grupo fica com o que a placa não diz (a divisão P + E,
        // as threads por núcleo e o clock de referência com o multiplicador).
        var (baseMhz, multiplier) = RatedClock(c.BaseMhz, c.RefClockMhz);
        var clocks = new List<SpecRow>();
        if (t.Hybrid) clocks.Add(new("Desempenho + eficiência", $"{t.PerformanceCores} + {t.EfficientCores}", null, null, Tone.None,
            "Os de desempenho (P) rodam jogos e tarefas pesadas; os de eficiência (E), o que fica em segundo plano."));
        clocks.Add(new(c.Vendor == "AMD" ? "SMT" : "Hyper-Threading", smt ? "Ativado" : "Desligado",
            smt ? $"{N((double)threads / cores, "0.#")} threads por núcleo" : null, "Com ele, cada núcleo executa duas threads ao mesmo tempo."));
        if (c.RefClockMhz > 0)
            clocks.Add(new("Clock de referência", $"{c.RefClockMhz} MHz",
                baseMhz > 0 ? $"× {(multiplier is int mul ? mul.ToString(Br) : N((double)baseMhz / c.RefClockMhz, "0.#"))} no clock base" : null,
                "O clock base é este valor vezes o multiplicador do processador."));
        string baseTip = "A frequência garantida. Com carga ele passa disso (boost), conforme a temperatura e o consumo."
            + (baseMhz != c.BaseMhz ? $"\nO Windows mede {c.BaseMhz.ToString("N0", Br)} MHz; o valor vai ao múltiplo de {c.RefClockMhz} MHz mais próximo (× {multiplier}), como o fabricante especifica." : "");

        var cache = Fmt.CacheLines(c).Select(kv => new SpecRow(kv.Key, kv.Value, kv.Key == "L3" && l3.Count == 1 ? "compartilhado" : null)).ToList();

        var features = new List<SpecRow>();
        if (c.Hypervisor)
        {
            features.Add(Verdict($"Virtualização ({amd})", "Ativada", Tone.Ok, null, "Máquinas virtuais e o WSL 2 funcionam."));
            features.Add(new("Hyper-V / VBS", "Em uso", null, null, Tone.None, "O próprio Windows usa a virtualização para isolar partes sensíveis do sistema."));
        }
        else if (c.Virtualization is bool on)
            features.Add(on
                ? Verdict($"Virtualização ({amd})", "Ativada", Tone.Ok, null, "Máquinas virtuais e o WSL 2 funcionam.")
                : Verdict($"Virtualização ({amd})", "Desligada", Tone.Warn, null, $"Para usar máquinas virtuais ou WSL 2, ative {amd} na BIOS."));

        var groups = new List<SpecGroup>
        {
            new("Identificação", id),
        };
        if (clocks.Count > 0) groups.Insert(0, new("Núcleos e clock", clocks, Help: "Cada núcleo é um processador completo. Threads são as tarefas que os núcleos executam ao mesmo tempo."));
        if (cache.Count > 0) groups.Add(new("Cache", cache, Help: "Memória dentro do processador, bem mais rápida que a RAM. L1 e L2 são de cada núcleo."));
        if (features.Count > 0) groups.Add(new("Recursos", features));
        if (c.Instructions.Count > 0)
            groups.Add(new("Instruções", new(), c.Instructions.Select(i => new SpecToken(i.Name, i.Supported)).ToList(),
                "Conjuntos de instruções que programas e jogos podem exigir." + (c.Instructions.Any(i => !i.Supported) ? " Em cinza, as que este processador não tem." : "")));

        return new Sheet
        {
            Key = Cpu,
            Eyebrow = c.Vendor,
            Title = HardwareNaming.ShortCpuName(c.Name) is { Length: > 0 } shortName ? shortName : c.Name,
            Subtitle = c.Name,
            Readouts = new()
            {
                new("Núcleos", cores.ToString(Br), "", t.Hybrid ? $"{t.PerformanceCores} de desempenho + {t.EfficientCores} de eficiência" : null),
                new("Threads", threads.ToString(Br), "", "Tarefas que os núcleos executam ao mesmo tempo."),
                new("Clock base", baseMhz > 0 ? N(baseMhz / 1000.0, "0.00") : "—", baseMhz > 0 ? "GHz" : "", baseMhz > 0 ? baseTip : null),
                new("Cache L3", l3Value, l3Unit, "Memória dentro do processador, bem mais rápida que a RAM."),
            },
            Groups = groups,
            Link = new("Clocks, temperatura e consumo ao vivo em Sensores", "PageSensors:cpu"),
        };
    }

    /// <summary>
    /// Clock base como o fabricante escreve. O Windows informa uma medida (2.793 MHz numa máquina virtual, 3.701 num Ryzen);
    /// quando ela fica a menos de 1 % de um múltiplo do clock de referência, vale o múltiplo, que é o multiplicador inteiro do
    /// chip (2.800 MHz, × 28). Longe disso, fica a medida, sem arredondar.
    /// </summary>
    private static (int Mhz, int? Multiplier) RatedClock(int mhz, int refMhz)
    {
        if (mhz <= 0 || refMhz <= 0) return (mhz, null);
        int m = (int)Math.Round((double)mhz / refMhz);
        return m > 0 && Math.Abs(m * refMhz - mhz) < mhz * 0.01 ? (m * refMhz, m) : (mhz, null);
    }
}
