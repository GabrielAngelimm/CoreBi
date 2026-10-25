using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CoreBi.Engine.Monitoring.Inventory;

/// <summary>Coleta informações estáticas do sistema (WMI, registro e APIs do Windows).</summary>
public static class SystemInfoCollector
{
    /// <summary>Árvore chave/valor das seis categorias (relatório).</summary>
    public static List<InfoNode> Collect() => Read().ToNodes();

    public static SystemInventory Read()
    {
        var inv = new SystemInventory();
        bool hypervisor = false;
        Safe(inv, SystemInventory.OsTitle, () => inv.Os = ReadOs(out hypervisor));
        Safe(inv, SystemInventory.BoardTitle, () => { inv.Board = ReadBoard(); inv.Firmware = ReadFirmware(); });
        Safe(inv, SystemInventory.CpuTitle, () => inv.Cpus.AddRange(ReadCpus(hypervisor)));
        Safe(inv, SystemInventory.RamTitle, () => inv.Ram = ReadRam());
        Safe(inv, SystemInventory.GpuTitle, () => inv.Gpus.AddRange(ReadGpus()));
        Safe(inv, SystemInventory.GpuTitle, () => inv.Displays.AddRange(DisplayReader.Read()));
        Safe(inv, SystemInventory.StorageTitle, () => inv.Disks = ReadDisks());
        Safe(inv, SystemInventory.StorageTitle, () => inv.BitLocker = ReadBitLocker());
        Safe(inv, SystemInventory.OsTitle, () => inv.Settings = ReadSettings());
        return inv;
    }

    private static void Safe(SystemInventory inv, string title, Action read)
    {
        try { read(); }
        catch (Exception ex) { inv.Errors.TryAdd(title, ex.Message); }
    }

    private static IEnumerable<ManagementObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        foreach (ManagementObject mo in searcher.Get())
            yield return mo;
    }

    private static string S(ManagementBaseObject mo, string prop)
        => mo[prop]?.ToString()?.Trim() is { Length: > 0 } s ? s : "—";

    private static int I(object? v) => v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);

    private static DateTime? CimDate(object? v)
    {
        try { return v is string s && s.Length >= 14 ? ManagementDateTimeConverter.ToDateTime(s) : null; }
        catch { return null; }
    }

    // ---------------- Sistema operacional ----------------

    private static OsInfo ReadOs(out bool hypervisor)
    {
        hypervisor = false;
        string caption = "Windows", build = "—";
        DateTime? installed = null, boot = null;
        foreach (var os in Query("SELECT Caption, BuildNumber, InstallDate, LastBootUpTime FROM Win32_OperatingSystem"))
        {
            caption = S(os, "Caption").Replace("Microsoft ", "");
            build = S(os, "BuildNumber");
            installed = CimDate(os["InstallDate"]);
            boot = CimDate(os["LastBootUpTime"]);
        }

        string version = "—", edition = caption;
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            if (key?.GetValue("DisplayVersion") is string dv) version = dv;
            else if (key?.GetValue("ReleaseId") is string rid) version = rid;
            if (key?.GetValue("UBR") is int ubr && build != "—") build = $"{build}.{ubr}";
        }

        string user = Environment.UserName, group = "—";
        bool domain = false;
        try
        {
            foreach (var cs in Query("SELECT Domain, PartOfDomain, Workgroup, HypervisorPresent FROM Win32_ComputerSystem"))
            {
                domain = cs["PartOfDomain"] is bool d && d;
                group = domain ? S(cs, "Domain") : (cs["Workgroup"]?.ToString() ?? S(cs, "Domain"));
                hypervisor = cs["HypervisorPresent"] is bool h && h;
            }
        }
        catch { }

        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "64 bits (x64)",
            Architecture.Arm64 => "64 bits (ARM64)",
            Architecture.X86 => "32 bits (x86)",
            var a => a.ToString()
        };

        // Última atualização instalada (KB mais recente). As datas vêm em M/d/yyyy, independente do idioma.
        string? kb = null; DateTime? updated = null;
        try
        {
            foreach (var q in Query("SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering"))
                if (q["InstalledOn"]?.ToString() is string on &&
                    DateTime.TryParse(on, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var when) &&
                    (updated == null || when > updated))
                {
                    updated = when;
                    kb = q["HotFixID"]?.ToString();
                }
        }
        catch { }

        return new OsInfo(caption, edition, version, build, arch, installed, boot, Environment.MachineName, user,
            group, domain, CultureInfo.InstalledUICulture.NativeName, kb, updated);
    }

    // ---------------- Placa-mãe e firmware ----------------

    private static BoardInfo ReadBoard()
    {
        string maker = "—", model = "—", rev = "—", serial = "—";
        foreach (var b in Query("SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard"))
        {
            maker = Fmt.Clean(S(b, "Manufacturer"));
            model = Fmt.Clean(S(b, "Product"));
            rev = Fmt.Clean(S(b, "Version"));
            serial = Fmt.Clean(S(b, "SerialNumber"));
        }
        string? sysMaker = null, sysModel = null, chassis = null;
        try
        {
            foreach (var cs in Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            {
                sysMaker = Fmt.IsPlaceholder(cs["Manufacturer"]?.ToString()) ? null : S(cs, "Manufacturer");
                sysModel = Fmt.IsPlaceholder(cs["Model"]?.ToString()) ? null : S(cs, "Model");
            }
            foreach (var e in Query("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                if (e["ChassisTypes"] is ushort[] { Length: > 0 } types)
                    chassis = types[0] switch
                    {
                        3 or 4 or 5 or 6 or 7 or 15 or 16 => "Desktop",
                        8 or 9 or 10 or 14 => "Notebook",
                        13 => "Tudo em um",
                        30 => "Tablet",
                        31 or 32 => "Conversível",
                        35 or 36 => "Mini PC",
                        _ => null
                    };
        }
        catch { }
        return new BoardInfo(maker, model, rev, serial, sysMaker, sysModel, chassis);
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetFirmwareType(out int type);

    private static FirmwareInfo ReadFirmware()
    {
        string vendor = "—", version = "—";
        string? smbios = null;
        DateTime? date = null;
        foreach (var b in Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SMBIOSMajorVersion, SMBIOSMinorVersion FROM Win32_BIOS"))
        {
            vendor = Fmt.Clean(S(b, "Manufacturer"));
            version = Fmt.Clean(S(b, "SMBIOSBIOSVersion"));
            string raw = WmiCalendarDate.Format(b["ReleaseDate"] as string);
            if (DateTime.TryParseExact(raw, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) date = d;
            if (b["SMBIOSMajorVersion"] != null) smbios = $"{I(b["SMBIOSMajorVersion"])}.{I(b["SMBIOSMinorVersion"])}";
        }

        bool? uefi = null;
        try { if (GetFirmwareType(out int t) && t is 1 or 2) uefi = t == 2; } catch { }

        bool? secure = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key?.GetValue("UEFISecureBootEnabled") is int v) secure = v == 1;
            else if (uefi == false) secure = false;
        }
        catch { }

        // TPM: precisa de administrador (o CoreBi roda elevado). SpecVersion = "2.0, 0, 1.59".
        string? tpm = null, tpmMaker = null, tpmFirmware = null; bool? ready = null;
        try
        {
            foreach (var t in Query("SELECT * FROM Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm"))
            {
                tpm = t["SpecVersion"]?.ToString()?.Split(',')[0].Trim();
                ready = t["IsEnabled_InitialValue"] is bool e && e && (t["IsActivated_InitialValue"] is not bool a || a);
                tpmMaker = t["ManufacturerIdTxt"]?.ToString()?.Trim() is { Length: > 0 } mk ? mk : null;
                tpmFirmware = t["ManufacturerVersion"]?.ToString()?.Trim() is { Length: > 0 } fw ? fw : null;
            }
            tpm ??= "Ausente";
        }
        catch { }
        return new FirmwareInfo(vendor, version, date, smbios, uefi, secure, tpm, ready, tpmMaker, tpmFirmware);
    }

    // ---------------- Processador ----------------

    private static readonly Regex FamilyModel = new(@"Family (\d+) Model (\d+) Stepping (\d+)", RegexOptions.Compiled);

    private static List<CpuInfo> ReadCpus(bool hypervisor)
    {
        var topo = CpuTopology.Read();
        var list = new List<CpuInfo>();
        foreach (var c in Query("SELECT * FROM Win32_Processor"))
        {
            string vendor = S(c, "Manufacturer") switch
            {
                "AuthenticAMD" => "AMD",
                "GenuineIntel" => "Intel",
                var v => Fmt.ShortMaker(v)
            };
            var fm = FamilyModel.Match(S(c, "Caption"));
            string arch = I(c["Architecture"]) switch { 9 => "x64", 12 => "ARM64", 0 => "x86", 5 => "ARM", _ => "—" };
            bool? virt = c["VirtualizationFirmwareEnabled"] is bool v2 ? v2 : null;
            int cores = I(c["NumberOfCores"]), threads = I(c["NumberOfLogicalProcessors"]);
            list.Add(new CpuInfo(
                Regex.Replace(S(c, "Name"), @"\s+", " "), vendor, Fmt.Clean(S(c, "SocketDesignation")), cores, threads, I(c["MaxClockSpeed"]),
                fm.Success ? int.Parse(fm.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                fm.Success ? int.Parse(fm.Groups[2].Value, CultureInfo.InvariantCulture) : null,
                fm.Success ? int.Parse(fm.Groups[3].Value, CultureInfo.InvariantCulture) : null,
                arch, virt, hypervisor, I(c["L2CacheSize"]), I(c["L3CacheSize"]),
                // A topologia do Windows cobre todos os soquetes; com um processador só (o caso comum) ela é dele.
                topo.Cores.Count == cores ? topo : new CpuTopology(),
                Instructions(), Microcode(), I(c["ExtClock"])));
        }
        return list;
    }

    /// <summary>
    /// Revisão do microcódigo carregado, em "Update Revision". Na Intel ela vem nos 4 últimos dos 8 bytes; na AMD, nos 4 primeiros.
    /// </summary>
    private static string? Microcode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("Update Revision") is byte[] { Length: >= 4 } b)
            {
                uint high = b.Length >= 8 ? BitConverter.ToUInt32(b, 4) : 0, low = BitConverter.ToUInt32(b, 0);
                if ((high != 0 ? high : low) is uint rev and > 0) return $"0x{rev:X8}";
            }
        }
        catch { }
        return null;
    }

    /// <summary>Conjuntos de instruções que o processador e o Windows habilitam (os que jogos e emuladores exigem).</summary>
    private static List<(string, bool)> Instructions()
    {
        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.X86)) return new();
        return new()
        {
            ("SSE4.2", Sse42.IsSupported),
            ("AVX", Avx.IsSupported),
            ("AVX2", Avx2.IsSupported),
            ("FMA3", Fma.IsSupported),
            ("AVX-512", Avx512F.IsSupported),
            ("AES-NI", System.Runtime.Intrinsics.X86.Aes.IsSupported),
            ("BMI2", Bmi2.IsSupported),
            ("POPCNT", Popcnt.IsSupported),
        };
    }

    // ---------------- Memória RAM ----------------

    private static string RamType(int smbios, int legacy) => smbios switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => legacy switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", _ => "—" }
    };

    private static string RamForm(int ff) => ff switch
    {
        8 => "DIMM",
        12 => "SO-DIMM",
        13 => "Micro-DIMM",
        15 => "FB-DIMM",
        9 => "RIMM",
        11 => "SRIMM",
        6 => "Proprietário",
        0 => "—",
        _ => "Outro"
    };

    // Velocidades que costumam aparecer no part number do módulo (ex.: CMK32GX5M2B6000C30, F5-6000J3038F16GX2).
    private static readonly Regex SpeedInPart = new(
        @"(?<!\d)(2133|2400|2666|2933|3000|3200|3466|3600|3733|3800|4000|4133|4266|4400|4800|5200|5600|5800|6000|6200|6400|6600|6800|7000|7200|7400|7600|7800|8000|8200|8400|8800)(?!\d)",
        RegexOptions.Compiled);

    // DIMM_A2, DIMM A1, ChannelA-DIMM0, Controller0-ChannelB-DIMM1, P0 CHANNEL A / DIMM 0
    private static readonly Regex SlotName = new(
        @"(?i)\b(?:channel|chan|ch)[\s_-]*([A-H])\W*(?:dimm|slot)?[\s_-]*(\d)?|dimm[\s_-]*([A-H])[\s_-]*(\d)|(?:^|[^A-Za-z])([A-H])(\d)(?:$|[^0-9])",
        RegexOptions.Compiled);

    private static (string? Channel, int? Index) ParseSlot(string locator, string bank)
    {
        foreach (var text in new[] { locator, $"{bank} {locator}" })
        {
            var m = SlotName.Match(text);
            if (!m.Success) continue;
            for (int g = 1; g <= 5; g += 2)
                if (m.Groups[g].Success)
                {
                    string ch = m.Groups[g].Value.ToUpperInvariant();
                    int? idx = m.Groups[g + 1].Success ? int.Parse(m.Groups[g + 1].Value, CultureInfo.InvariantCulture) : null;
                    if (idx == null && Regex.Match(locator, @"(?i)dimm[\s_-]*(\d)") is { Success: true } d)
                        idx = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
                    return (ch, idx);
                }
        }
        return (null, null);
    }

    private static double? Volts(object? mv) => mv == null || Convert.ToDouble(mv, CultureInfo.InvariantCulture) <= 0
        ? null : Convert.ToDouble(mv, CultureInfo.InvariantCulture) / 1000;

    private static RamInfo ReadRam()
    {
        var mods = new List<RamModule>();
        int partSpeed = 0;
        foreach (var m in Query("SELECT * FROM Win32_PhysicalMemory"))
        {
            string bank = S(m, "BankLabel"), loc = S(m, "DeviceLocator");
            var (ch, idx) = ParseSlot(loc, bank);
            string part = S(m, "PartNumber");
            if (SpeedInPart.Match(part) is { Success: true } sp) partSpeed = Math.Max(partSpeed, int.Parse(sp.Value, CultureInfo.InvariantCulture));
            mods.Add(new RamModule(bank, loc, ch != null && idx != null ? $"{ch}{idx}" : loc, ch, idx,
                Convert.ToUInt64(m["Capacity"] ?? 0UL, CultureInfo.InvariantCulture),
                RamType(I(m["SMBIOSMemoryType"]), I(m["MemoryType"])), RamForm(I(m["FormFactor"])),
                I(m["Speed"]), I(m["ConfiguredClockSpeed"]), Fmt.ShortMaker(Fmt.Clean(S(m, "Manufacturer"))), Fmt.Clean(part), Fmt.Clean(S(m, "SerialNumber")),
                I(m["DataWidth"]), I(m["TotalWidth"]), I(m["Attributes"]) & 0xF,
                Volts(m["ConfiguredVoltage"]), Volts(m["MinVoltage"]), Volts(m["MaxVoltage"]), I(m["TypeDetail"])));
        }

        int slots = 0; ulong maxBytes = 0;
        try
        {
            foreach (var a in Query("SELECT MemoryDevices, MaxCapacity, MaxCapacityEx FROM Win32_PhysicalMemoryArray"))
            {
                slots += I(a["MemoryDevices"]);
                ulong kb = a["MaxCapacityEx"] != null ? Convert.ToUInt64(a["MaxCapacityEx"], CultureInfo.InvariantCulture) : Convert.ToUInt64(a["MaxCapacity"] ?? 0UL, CultureInfo.InvariantCulture);
                maxBytes += kb * 1024;
            }
        }
        catch { }

        ulong virt = 0;
        try
        {
            foreach (var os in Query("SELECT TotalVirtualMemorySize FROM Win32_OperatingSystem"))
                virt = Convert.ToUInt64(os["TotalVirtualMemorySize"] ?? 0UL, CultureInfo.InvariantCulture) * 1024;
        }
        catch { }

        // Arquivo de paginação: tamanho reservado e uso (MB), e se o Windows gerencia o tamanho sozinho.
        var pages = new List<PageFile>();
        bool? auto = null;
        try
        {
            foreach (var pf in Query("SELECT Name, AllocatedBaseSize, CurrentUsage, PeakUsage FROM Win32_PageFileUsage"))
                pages.Add(new PageFile(S(pf, "Name"), I(pf["AllocatedBaseSize"]), I(pf["CurrentUsage"]), I(pf["PeakUsage"])));
            foreach (var cs in Query("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
                auto = cs["AutomaticManagedPagefile"] as bool?;
        }
        catch { }

        return new RamInfo { Modules = mods, Slots = slots, MaxCapacity = maxBytes, VirtualTotal = virt, PartSpeed = partSpeed, PageFiles = pages, PageFileAuto = auto };
    }

    // ---------------- Placa de vídeo ----------------

    private static List<VideoAdapter> ReadGpus()
    {
        var vram = VramByName();
        var list = new List<VideoAdapter>();
        foreach (var g in Query("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController"))
        {
            string name = S(g, "Name"), win = S(g, "DriverVersion"), vendorRaw = S(g, "AdapterCompatibility");
            string vendor = vendorRaw.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA" : Fmt.ShortMaker(vendorRaw);
            if (S(g, "PNPDeviceID").StartsWith("ROOT", StringComparison.OrdinalIgnoreCase) && name.Contains("Basic", StringComparison.OrdinalIgnoreCase)) continue;
            DateTime? date = null;
            if (g["DriverDate"] is string dd && DateTime.TryParseExact(WmiCalendarDate.Format(dd), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) date = d;
            list.Add(new VideoAdapter(name, vendor, vendor == "NVIDIA" ? NvidiaVersion(win) : win, win, date,
                vram.TryGetValue(name, out var v) ? v : null));
        }
        return list.OrderByDescending(g => g.Vram ?? 0).ToList();
    }

    /// <summary>32.0.15.6636 → 566.36 (a versão que a NVIDIA divulga).</summary>
    internal static string NvidiaVersion(string windows)
    {
        var parts = windows.Split('.');
        if (parts.Length < 4) return windows;
        string digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5) return windows;
        digits = digits[^5..];
        return $"{int.Parse(digits[..3], CultureInfo.InvariantCulture)}.{digits[3..]}";
    }

    /// <summary>VRAM em 64 bits pelo registro do driver (o WMI corta em 4 GB).</summary>
    private static Dictionary<string, ulong> VramByName()
    {
        var map = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return map;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is not string desc) continue;
                    ulong bytes = k.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long l => (ulong)l,
                        byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
                        _ => k.GetValue("HardwareInformation.MemorySize") switch
                        {
                            int i => (uint)i,
                            byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                            _ => 0
                        }
                    };
                    if (bytes > 0 && (!map.TryGetValue(desc, out var old) || bytes > old)) map[desc] = bytes;
                }
                catch { }
            }
        }
        catch { }
        return map;
    }

    // ---------------- Armazenamento ----------------

    /// <summary>Ajustes do Windows que pesam em jogos, lidos do registro (null quando a chave não existe).</summary>
    private static WindowsSettings ReadSettings()
    {
        static int? Dword(RegistryKey root, string path, string name)
        {
            try { using var k = root.OpenSubKey(path); return k?.GetValue(name) is int v ? v : null; }
            catch { return null; }
        }
        int? gameMode = Dword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
        int? hags = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        int? hvci = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
        int? fast = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled");
        int? capture = Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
        bool? mouse = null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            if (k?.GetValue("MouseSpeed") is string ms) mouse = ms != "0";
        }
        catch { }
        return new WindowsSettings(
            gameMode != 0,                              // sem a chave, o Modo de jogo vem ligado
            hags switch { 2 => true, 1 => false, _ => null },
            hvci is int h ? h == 1 : false,
            fast is int f ? f == 1 : null,
            TimeZoneInfo.Local.DisplayName,
            capture is int c ? c == 1 : null,
            mouse,
            PowerPlan(),
            Activated());
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr root, IntPtr scheme, IntPtr subGroup, IntPtr setting, byte[]? buffer, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    /// <summary>Nome do plano de energia ativo ("Equilibrado", "Alto desempenho"...).</summary>
    private static string? PowerPlan()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var scheme) != 0) return null;
            try
            {
                uint size = 0;
                PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
                if (size == 0) return null;
                var buffer = new byte[size];
                if (PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0) return null;
                string name = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
                return name.Length > 0 ? name : null;
            }
            finally { LocalFree(scheme); }
        }
        catch { return null; }
    }

    /// <summary>Licença do Windows ativada (consulta lenta, por isso fica fora da thread da janela, como o resto).</summary>
    private static bool? Activated()
    {
        try
        {
            foreach (var l in Query("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL"))
                if (I(l["LicenseStatus"]) == 1) return true;
            return false;
        }
        catch { return null; }
    }

    /// <summary>BitLocker por letra de unidade (precisa de administrador; o CoreBi roda elevado).</summary>
    public static Dictionary<string, bool> ReadBitLocker()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var v in Query("SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume", @"root\CIMV2\Security\MicrosoftVolumeEncryption"))
                if (v["DriveLetter"]?.ToString() is { Length: > 0 } letter && I(v["ProtectionStatus"]) is 0 or 1)
                    map[letter] = I(v["ProtectionStatus"]) == 1;
        }
        catch { }
        return map;
    }

    /// <summary>Relê os discos físicos (ex.: depois de conectar um pendrive ou HD externo).</summary>
    public static List<PhysicalDiskInfo> ReadDisks()
    {
        var media = new Dictionary<int, (string Bus, string Media, string? Health, int? Rpm)>();
        try
        {
            foreach (var d in Query("SELECT DeviceId, BusType, MediaType, HealthStatus, SpindleSpeed FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
                if (int.TryParse(d["DeviceId"]?.ToString(), out int idx))
                {
                    long rpm = d["SpindleSpeed"] is { } sp ? Convert.ToInt64(sp, CultureInfo.InvariantCulture) : 0;
                    media[idx] = (I(d["BusType"]) switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 10 => "SAS", 8 => "RAID", 12 => "SD", 6 => "Fibre Channel", 3 => "ATA", _ => "" },
                                  I(d["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "" },
                                  I(d["HealthStatus"]) switch { 0 => "Saudável", 1 => "Atenção", 2 => "Com defeito", _ => null },
                                  rpm is > 0 and < 100000 ? (int)rpm : null);
                }
        }
        catch { }

        // Estilo de partição (GPT/MBR), pelo número do disco.
        // Também setores, quantas partições e se é o disco de onde o Windows inicia.
        var partition = new Dictionary<int, string>();
        var layout = new Dictionary<int, (int Logical, int Physical, int Parts, bool System)>();
        try
        {
            foreach (var d in Query("SELECT Number, PartitionStyle, LogicalSectorSize, PhysicalSectorSize, NumberOfPartitions, IsSystem, IsBoot FROM MSFT_Disk", @"root\Microsoft\Windows\Storage"))
            {
                if (d["Number"] == null) continue;
                int n = I(d["Number"]);
                if (I(d["PartitionStyle"]) is 1 or 2) partition[n] = I(d["PartitionStyle"]) == 2 ? "GPT" : "MBR";
                layout[n] = (I(d["LogicalSectorSize"]), I(d["PhysicalSectorSize"]), I(d["NumberOfPartitions"]),
                    d["IsSystem"] is true || d["IsBoot"] is true);
            }
        }
        catch { }

        var list = new List<PhysicalDiskInfo>();
        foreach (var d in Query("SELECT Index, Model, Size, InterfaceType, SerialNumber, FirmwareRevision, PNPDeviceID, MediaType FROM Win32_DiskDrive"))
        {
            int idx = I(d["Index"]);
            bool usb = S(d, "PNPDeviceID").StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase);
            media.TryGetValue(idx, out var m);
            layout.TryGetValue(idx, out var lay);
            string bus = m.Bus is { Length: > 0 } b ? b : usb ? "USB" : S(d, "InterfaceType");
            bool removable = usb || bus == "USB" || S(d, "MediaType").Contains("Removable", StringComparison.OrdinalIgnoreCase);
            list.Add(new PhysicalDiskInfo(idx, S(d, "Model"), Convert.ToUInt64(d["Size"] ?? 0UL, CultureInfo.InvariantCulture), bus, m.Media ?? "",
                Fmt.Clean(S(d, "FirmwareRevision")), Fmt.Clean(S(d, "SerialNumber")), removable,
                m.Health, partition.GetValueOrDefault(idx), m.Rpm, lay.Logical, lay.Physical, lay.Parts, lay.System));
        }
        return list.OrderBy(d => d.Index).ToList();
    }
}
