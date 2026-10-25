using System.Globalization;

namespace CoreBi.Engine.Monitoring.Inventory;

/// <summary>Veredito curto mostrado ao lado de um dado (verde ou âmbar; nunca enfeite).</summary>
public enum Tone { None, Ok, Warn }

public sealed record Finding(Tone Tone, string Title, string Text);

public sealed record OsInfo(
    string Name, string Edition, string Version, string Build, string Architecture,
    DateTime? Installed, DateTime? LastBoot, string Machine, string User, string Workgroup, bool Domain,
    string Language, string? LastUpdateId, DateTime? LastUpdate);

public sealed record BoardInfo(string Maker, string Model, string Revision, string Serial, string? SystemMaker, string? SystemModel);

public sealed record FirmwareInfo(string Vendor, string Version, DateTime? Date, string? Smbios, bool? Uefi, bool? SecureBoot, string? Tpm, bool? TpmReady)
{
    /// <summary>UEFI, Secure Boot e TPM 2.0: o que o Windows 11 exige do firmware.</summary>
    public bool? Windows11Ready => Uefi == null || SecureBoot == null || Tpm == null
        ? null : Uefi == true && SecureBoot == true && Tpm.StartsWith("2") && TpmReady != false;
}

public sealed record CpuInfo(
    string Name, string Vendor, string Socket, int Cores, int Threads, int BaseMhz,
    int? Family, int? Model, int? Stepping, string Architecture, bool? Virtualization, bool Hypervisor,
    long L2Kb, long L3Kb, CpuTopology Topology, List<(string Name, bool Supported)> Instructions)
{
    public Finding? VirtualizationFinding => Hypervisor
        ? new(Tone.Ok, "Virtualização em uso pelo Windows", "(Hyper-V ou segurança baseada em virtualização). Máquinas virtuais e WSL 2 funcionam.")
        : Virtualization switch
        {
            true => new(Tone.Ok, "Virtualização ativada", $"no firmware ({(Vendor == "AMD" ? "SVM" : "VT-x")}). Máquinas virtuais e WSL 2 funcionam."),
            false => new(Tone.Warn, "Virtualização desligada", $"no firmware. Para usar máquinas virtuais ou WSL 2, ative {(Vendor == "AMD" ? "SVM" : "VT-x")} na BIOS."),
            _ => null
        };
}

public sealed record RamModule(
    string Bank, string Locator, string Slot, string? Channel, int? SlotIndex, ulong Capacity, string Type, string Form,
    int Rated, int Configured, string Maker, string Part, string Serial, int DataWidth, int TotalWidth, int Ranks,
    double? Volt, double? MinVolt, double? MaxVolt);

/// <summary>Um slot do desenho da placa: ocupado (com o módulo) ou vazio.</summary>
public sealed record RamSlot(string Name, string? Channel, RamModule? Module);

public sealed class RamInfo
{
    public List<RamModule> Modules { get; init; } = new();
    public int Slots { get; init; }
    public ulong MaxCapacity { get; init; }
    public ulong VirtualTotal { get; init; }
    public ulong Total => (ulong)Modules.Sum(m => (decimal)m.Capacity);
    public int Configured => Modules.Where(m => m.Configured > 0).Select(m => m.Configured).DefaultIfEmpty(0).Min();
    public int Rated => Modules.Select(m => m.Rated).DefaultIfEmpty(0).Max();
    public string Type => string.Join(" / ", Modules.Select(m => m.Type).Distinct());
    public bool Ecc => Modules.Any(m => m.TotalWidth > m.DataWidth && m.DataWidth > 0);

    /// <summary>Velocidade escrita no part number (ex.: CMK32GX5M2B6000C30, F4-3600C16-16GVKC), 0 se não houver.</summary>
    public int PartSpeed { get; init; }

    /// <summary>Canais distintos deduzidos pelos nomes dos slots; null quando os nomes não dizem.</summary>
    public int? Channels
    {
        get
        {
            if (Modules.Count == 0 || Modules.Any(m => m.Channel == null)) return Modules.Count == 1 ? 1 : null;
            return Modules.Select(m => m.Channel).Distinct().Count();
        }
    }

    /// <summary>EXPO/XMP: o Windows não diz se está ligado; estimamos pela velocidade escrita no part number.</summary>
    public Finding? Profile
    {
        get
        {
            int conf = Configured;
            if (Modules.Count == 0) return null;
            if (conf <= 0) return new(Tone.None, "Perfil XMP/EXPO:", "não foi possível ler a velocidade em uso.");
            if (PartSpeed > 0 && conf < PartSpeed * 0.95)
                return new(Tone.Warn, "XMP/EXPO provavelmente desligado:", $"a RAM roda a {conf} MT/s, mas o part number indica {PartSpeed} MT/s. Ative o perfil na BIOS.");
            if (PartSpeed > 0)
                return new(Tone.Ok, "XMP/EXPO provavelmente ativo:", $"roda a {conf} MT/s, como indica o part number.");
            if (Rated > 0 && conf < Rated)
                return new(Tone.Warn, "Abaixo da velocidade nominal:", $"roda a {conf} de {Rated} MT/s. Confira XMP/EXPO na BIOS.");
            return new(Tone.None, "Perfil XMP/EXPO:", "sem como estimar (o part number não traz a velocidade). Confira na BIOS.");
        }
    }

    public Finding? ChannelFinding
    {
        get
        {
            if (Modules.Count == 0) return null;
            if (Modules.Count == 1)
                return new(Tone.Warn, "Canal único:", "com um só módulo a RAM não roda em dual channel. Dois módulos iguais deixam a memória bem mais rápida.");
            return Channels switch
            {
                null => new(Tone.None, "Canais:", $"os nomes dos slots ({string.Join(", ", Modules.Select(m => m.Locator))}) não dizem. Veja o manual da placa."),
                1 => new(Tone.Warn, "Canal único:", "os módulos estão no mesmo canal. Mude para os slots recomendados no manual da placa."),
                2 => new(Tone.Ok, "Dual channel:", Modules.Count == 2 ? "um módulo em cada canal." : "os módulos ocupam os dois canais."),
                int n => new(Tone.Ok, $"{n} canais:", "os módulos ocupam canais diferentes.")
            };
        }
    }

    /// <summary>
    /// Desenho dos slots: com nomes no padrão A1/A2/B1/B2 (ou ChannelA-DIMM0) completa os vazios; senão,
    /// mostra os ocupados com o nome lido e acrescenta os vazios sem nome.
    /// </summary>
    public List<RamSlot> SlotMap()
    {
        var map = new List<RamSlot>();
        bool parsed = Modules.Count > 0 && Modules.All(m => m.Channel != null && m.SlotIndex != null);
        int slots = Math.Max(Slots, Modules.Count);
        if (parsed && slots > Modules.Count && slots <= 8 && slots % 2 == 0)
        {
            int perChannel = slots >= 4 ? 2 : 1, channels = slots / perChannel;
            int first = Modules.Min(m => m.SlotIndex!.Value) == 0 ? 0 : 1;
            var letters = Enumerable.Range(0, channels).Select(i => ((char)('A' + i)).ToString()).ToList();
            if (Modules.All(m => letters.Contains(m.Channel!) && m.SlotIndex >= first && m.SlotIndex < first + perChannel))
            {
                foreach (var ch in letters)
                    for (int i = first; i < first + perChannel; i++)
                        map.Add(new RamSlot($"{ch}{i}", ch, Modules.FirstOrDefault(m => m.Channel == ch && m.SlotIndex == i)));
                return map;
            }
        }
        foreach (var m in Modules.OrderBy(m => m.Channel).ThenBy(m => m.SlotIndex)) map.Add(new RamSlot(m.Slot, m.Channel, m));
        for (int i = Modules.Count; i < slots; i++) map.Add(new RamSlot("", null, null));
        return map;
    }
}

public sealed record VideoAdapter(string Name, string Vendor, string DriverVersion, string WindowsDriverVersion, DateTime? DriverDate, ulong? Vram);

public sealed record PhysicalDiskInfo(int Index, string Model, ulong Size, string Bus, string Media, string Firmware, string Serial, bool Removable);

/// <summary>Tudo que a página Sistema mostra, lido uma vez (WMI, registro e APIs do Windows).</summary>
public sealed class SystemInventory
{
    public OsInfo? Os { get; set; }
    public BoardInfo? Board { get; set; }
    public FirmwareInfo? Firmware { get; set; }
    public List<CpuInfo> Cpus { get; } = new();
    public RamInfo? Ram { get; set; }
    public List<VideoAdapter> Gpus { get; } = new();
    public List<DisplayInfo> Displays { get; } = new();
    public List<PhysicalDiskInfo> Disks { get; set; } = new();

    /// <summary>Categoria → mensagem, quando a leitura dela falhou.</summary>
    public Dictionary<string, string> Errors { get; } = new();

    public const string OsTitle = "Sistema operacional", BoardTitle = "Placa-mãe e BIOS", CpuTitle = "Processador",
                        RamTitle = "Memória RAM", GpuTitle = "Placa de vídeo", StorageTitle = "Armazenamento";

    /// <summary>Árvore chave/valor para o relatório exportado (mesmas categorias da página).</summary>
    public List<InfoNode> ToNodes()
    {
        var roots = new List<InfoNode>();
        InfoNode Root(string title)
        {
            var n = new InfoNode { Header = title };
            if (Errors.TryGetValue(title, out var err)) n.Add("Erro ao ler", err);
            roots.Add(n);
            return n;
        }

        var os = Root(OsTitle);
        if (Os is { } o)
        {
            os.Add("Nome", o.Name);
            os.Add("Versão", o.Version);
            os.Add("Build", o.Build);
            os.Add("Arquitetura", o.Architecture);
            os.Add("Instalado em", Fmt.Date(o.Installed));
            os.Add("Último boot", Fmt.Stamp(o.LastBoot));
            if (o.LastUpdate != null) os.Add("Última atualização", $"{Fmt.Date(o.LastUpdate)} ({o.LastUpdateId})");
            os.Add("Nome do computador", o.Machine);
        }

        var board = Root(BoardTitle);
        if (Board is { } b)
        {
            var mb = board.Add("Placa-mãe");
            mb.Add("Fabricante", b.Maker);
            mb.Add("Modelo", b.Model);
            mb.Add("Revisão", b.Revision);
            mb.Add("Nº de série", b.Serial);
        }
        if (Firmware is { } f)
        {
            var bios = board.Add("BIOS");
            bios.Add("Fabricante", f.Vendor);
            bios.Add("Versão", f.Version);
            bios.Add("Data", Fmt.Date(f.Date));
            if (f.Smbios != null) bios.Add("SMBIOS", f.Smbios);
            bios.Add("Modo de inicialização", f.Uefi switch { true => "UEFI", false => "Legado (CSM)", _ => "—" });
            bios.Add("Secure Boot", Fmt.OnOff(f.SecureBoot));
            bios.Add("TPM", f.Tpm ?? "—");
        }

        var cpus = Root(CpuTitle);
        foreach (var c in Cpus)
        {
            var cpu = cpus.Add(c.Name);
            cpu.Add("Fabricante", c.Vendor);
            cpu.Add("Soquete", c.Socket);
            cpu.Add("Núcleos físicos", c.Cores.ToString(CultureInfo.CurrentCulture));
            if (c.Topology.Hybrid) cpu.Add("Desempenho / eficiência", $"{c.Topology.PerformanceCores} + {c.Topology.EfficientCores}");
            cpu.Add("Threads (lógicos)", c.Threads.ToString(CultureInfo.CurrentCulture));
            cpu.Add("Clock base", Fmt.Ghz(c.BaseMhz));
            foreach (var cache in Fmt.CacheLines(c)) cpu.Add(cache.Key, cache.Value);
            cpu.Add("Virtualização", c.Hypervisor ? "Em uso pelo Windows" : Fmt.OnOff(c.Virtualization));
            cpu.Add("Instruções", string.Join(" ", c.Instructions.Where(i => i.Supported).Select(i => i.Name)));
        }

        var ram = Root(RamTitle);
        if (Ram is { } r)
        {
            ram.Add("Total instalado", Fmt.Bytes(r.Total));
            var sum = ram.Add("Resumo");
            sum.Add("Módulos instalados", r.Slots > 0 ? $"{r.Modules.Count} de {r.Slots} slots" : r.Modules.Count.ToString(CultureInfo.CurrentCulture));
            if (r.MaxCapacity > 0) sum.Add("Capacidade máxima suportada pela placa", Fmt.Bytes(r.MaxCapacity));
            if (r.Modules.Count > 0)
            {
                sum.Add("Tipo", r.Type);
                sum.Add("Velocidade em uso", r.Configured > 0 ? $"{r.Configured} MT/s" : "—");
                sum.Add("Velocidade nominal (JEDEC)", r.Rated > 0 ? $"{r.Rated} MT/s" : "—");
                if (r.Profile is { } p) sum.Add("Perfil XMP/EXPO (estimado)", $"{p.Title} {p.Text}");
                if (r.ChannelFinding is { } ch) sum.Add("Canais (estimado)", $"{ch.Title} {ch.Text}");
                sum.Add("ECC", r.Ecc ? "Sim" : "Não");
            }
            if (r.VirtualTotal > 0) sum.Add("Memória virtual (RAM + paginação)", Fmt.Bytes(r.VirtualTotal));
            foreach (var m in r.Modules)
            {
                var slot = ram.Add($"Módulo {m.Slot}");
                slot.Add("Slot", $"{m.Bank} / {m.Locator}");
                slot.Add("Capacidade", Fmt.Bytes(m.Capacity));
                slot.Add("Tipo", m.Type);
                slot.Add("Formato", m.Form);
                slot.Add("Ranks", m.Ranks > 0 ? m.Ranks.ToString(CultureInfo.CurrentCulture) : "—");
                slot.Add("Velocidade em uso", m.Configured > 0 ? $"{m.Configured} MT/s" : "—");
                slot.Add("Velocidade nominal", m.Rated > 0 ? $"{m.Rated} MT/s" : "—");
                slot.Add("Tensão em uso", Fmt.Volt(m.Volt));
                slot.Add("Fabricante", m.Maker);
                slot.Add("Part number", m.Part);
                slot.Add("Nº de série", m.Serial);
            }
        }

        var gpus = Root(GpuTitle);
        foreach (var g in Gpus)
        {
            var gpu = gpus.Add(g.Name);
            gpu.Add("Versão do driver", g.DriverVersion == g.WindowsDriverVersion ? g.DriverVersion : $"{g.DriverVersion} ({g.WindowsDriverVersion})");
            gpu.Add("Data do driver", Fmt.Date(g.DriverDate));
            gpu.Add("Memória dedicada", g.Vram is ulong v && v > 0 ? Fmt.Vram(v) : "—");
        }
        foreach (var d in Displays)
        {
            var scr = gpus.Add($"Tela {d.Number}");
            scr.Add("Monitor", d.Monitor);
            scr.Add("Resolução", $"{d.Width} × {d.Height}");
            scr.Add("Atualização", d.BelowMax ? $"{d.Hz} Hz (aceita até {d.MaxHz} Hz)" : $"{d.Hz} Hz");
            scr.Add("Placa de vídeo", d.Adapter);
        }

        var storage = Root(StorageTitle);
        foreach (var d in Disks)
        {
            var disk = storage.Add(d.Model);
            disk.Add("Capacidade", Fmt.Bytes(d.Size));
            disk.Add("Interface", d.Media.Length > 0 ? $"{d.Bus} · {d.Media}" : d.Bus);
            disk.Add("Firmware", d.Firmware);
            disk.Add("Nº de série", d.Serial);
        }
        return roots;
    }
}

/// <summary>Formatação comum da página Sistema e do relatório (pt-BR: vírgula decimal, datas dd/MM/yyyy).</summary>
public static class Fmt
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Capacidade binária como o Windows mostra: 16 GB, 931 GB, 1,8 TB.</summary>
    public static string Bytes(double b)
    {
        if (b <= 0) return "—";
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        // Como o Explorador: acima de 100 corta as casas (1 TB = 931 GB, não 932).
        return b >= 100 ? string.Format(Br, "{0:0} {1}", Math.Floor(b), u[i]) : string.Format(Br, i < 3 ? "{0:0} {1}" : "{0:0.#} {1}", b, u[i]);
    }

    /// <summary>Número e unidade separados (para leituras grandes).</summary>
    public static (string Value, string Unit) BytesParts(double b)
    {
        string s = Bytes(b);
        int sp = s.LastIndexOf(' ');
        return sp > 0 ? (s[..sp], s[(sp + 1)..]) : (s, "");
    }

    /// <summary>VRAM: o driver reserva um pedaço; 8176 MB utilizáveis = placa de 8 GB.</summary>
    public static string Vram(ulong b) => b >= 1UL << 30 ? $"{Math.Ceiling(b / (double)(1UL << 30) - 0.15):0} GB" : Bytes(b);

    public static string Ghz(int mhz) => mhz > 0 ? string.Format(Br, "{0:0.00} GHz", mhz / 1000.0) : "—";
    public static string Volt(double? v) => v is double x && x > 0 ? string.Format(Br, "{0:0.00#} V", x) : "—";
    public static string OnOff(bool? v) => v switch { true => "Ativado", false => "Desativado", _ => "—" };
    public static string Date(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public static string Stamp(DateTime? d) => d?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—";

    /// <summary>"há 1 ano e 7 meses", "há 3 meses", "há 12 dias".</summary>
    public static string Ago(DateTime? d, DateTime? now = null)
    {
        if (d is not { } from) return "";
        var to = now ?? System.DateTime.Now;
        int months = (to.Year - from.Year) * 12 + to.Month - from.Month - (to.Day < from.Day ? 1 : 0);
        if (months < 1)
        {
            int days = Math.Max(0, (int)(to - from).TotalDays);
            return days switch { 0 => "hoje", 1 => "há 1 dia", _ => $"há {days} dias" };
        }
        int y = months / 12, m = months % 12;
        string ys = y == 1 ? "1 ano" : $"{y} anos", ms = m == 1 ? "1 mês" : $"{m} meses";
        return y == 0 ? $"há {ms}" : m == 0 ? $"há {ys}" : $"há {ys} e {ms}";
    }

    /// <summary>Duração curta: "4 h 32 min", "2 d 5 h", "12 min".</summary>
    public static string Span(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} d {t.Hours} h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        return $"{Math.Max(0, t.Minutes)} min";
    }

    public static string Cache(long bytes)
        => bytes >= 1 << 20 ? string.Format(Br, "{0:0.#} MB", bytes / (double)(1 << 20)) : $"{bytes / 1024} KB";

    /// <summary>"8 × 32 KB", ou "6 × 2 MB + 2 × 4 MB" quando núcleos diferentes têm caches diferentes.</summary>
    public static string CacheGroup(IEnumerable<CpuCache> group)
        => string.Join(" + ", group.OrderByDescending(c => c.Instances).Select(c => c.Instances > 1 ? $"{c.Instances} × {Cache(c.Size)}" : Cache(c.Size)));

    /// <summary>Linhas de cache para a ficha: da topologia do Windows ou, sem ela, os totais do WMI.</summary>
    public static List<KeyValuePair<string, string>> CacheLines(CpuInfo c)
    {
        var list = new List<KeyValuePair<string, string>>();
        var caches = c.Topology.Caches;
        if (caches.Count > 0)
        {
            foreach (var g in caches.GroupBy(x => (x.Level, x.Kind)).OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Kind == "dados" ? 0 : 1))
            {
                string name = g.Key.Kind is "dados" or "instruções" ? $"L{g.Key.Level} {g.Key.Kind}" : $"L{g.Key.Level}";
                list.Add(new(name, CacheGroup(g)));
            }
            return list;
        }
        if (c.L2Kb > 0) list.Add(new("L2", Cache(c.L2Kb * 1024)));
        if (c.L3Kb > 0) list.Add(new("L3", Cache(c.L3Kb * 1024)));
        return list;
    }

    /// <summary>Texto que o fabricante deixou de preencher (placas montadas, BIOS genérica).</summary>
    public static bool IsPlaceholder(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        string t = s.Trim().ToLowerInvariant();
        return t is "—" or "default string" or "to be filled by o.e.m." or "system product name" or "system manufacturer" or "system version"
            or "not applicable" or "not specified" or "none" or "n/a" or "0" or "base board serial number" or "123456789" or "unknown" or "x.x"
            || t.StartsWith("to be filled");
    }

    public static string Clean(string? s) => IsPlaceholder(s) ? "—" : s!.Trim();

    /// <summary>Nome curto do fabricante para o sobretítulo: "ASUSTeK COMPUTER INC." → "ASUS".</summary>
    public static string ShortMaker(string maker)
    {
        string m = maker.ToLowerInvariant();
        (string Key, string Name)[] known =
        {
            ("asus", "ASUS"), ("micro-star", "MSI"), ("msi", "MSI"), ("gigabyte", "Gigabyte"), ("asrock", "ASRock"),
            ("dell", "Dell"), ("hewlett", "HP"), ("hp ", "HP"), ("lenovo", "Lenovo"), ("acer", "Acer"), ("samsung", "Samsung"),
            ("biostar", "Biostar"), ("evga", "EVGA"), ("intel", "Intel"), ("advanced micro devices", "AMD"), ("nvidia", "NVIDIA"),
            ("microsoft", "Microsoft"), ("apple", "Apple"), ("g skill", "G.Skill"), ("g.skill", "G.Skill"), ("corsair", "Corsair"),
            ("kingston", "Kingston"), ("crucial", "Crucial"), ("micron", "Micron"), ("hynix", "SK hynix"), ("adata", "ADATA"),
            ("teamgroup", "TeamGroup"), ("team group", "TeamGroup"), ("patriot", "Patriot"), ("american megatrends", "AMI"),
        };
        foreach (var (key, name) in known) if (m.Contains(key)) return name;
        return maker.Trim();
    }
}
