using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Storage;
using Microsoft.Win32;

namespace CoreBi.Engine.Monitoring.Inventory;

internal static class HardwareInventoryReader
{
    private static readonly Regex FamilyModel = new(@"Family (\d+) Model (\d+) Stepping (\d+)", RegexOptions.Compiled);
    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex DimmNumber = new(@"(?i)dimm[\s_-]*(\d)", RegexOptions.Compiled);

    // Velocidades que costumam aparecer no part number do módulo (ex.: CMK32GX5M2B6000C30, F5-6000J3038F16GX2).
    private static readonly Regex SpeedInPart = new(
        @"(?<!\d)(2133|2400|2666|2933|3000|3200|3466|3600|3733|3800|4000|4133|4266|4400|4800|5200|5600|5800|6000|6200|6400|6600|6800|7000|7200|7400|7600|7800|8000|8200|8400|8800)(?!\d)",
        RegexOptions.Compiled);

    // DIMM_A2, DIMM A1, ChannelA-DIMM0, Controller0-ChannelB-DIMM1, P0 CHANNEL A / DIMM 0
    private static readonly Regex SlotName = new(
        @"(?i)\b(?:channel|chan|ch)[\s_-]*([A-H])\W*(?:dimm|slot)?[\s_-]*(\d)?|dimm[\s_-]*([A-H])[\s_-]*(\d)|(?:^|[^A-Za-z])([A-H])(\d)(?:$|[^0-9])",
        RegexOptions.Compiled);

    [DllImport("kernel32.dll")]
    private static extern bool GetFirmwareType(out int type);

    public static BoardInfo ReadBoard(ComputerSystemSnapshot computerSystem)
    {
        string maker = "—", model = "—", rev = "—", serial = "—";
        WmiQuery.ForEachOrThrow("SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard", b =>
        {
            maker = Fmt.Clean(WmiQuery.GetString(b, "Manufacturer"));
            model = Fmt.Clean(WmiQuery.GetString(b, "Product"));
            rev = Fmt.Clean(WmiQuery.GetString(b, "Version"));
            serial = Fmt.Clean(WmiQuery.GetString(b, "SerialNumber"));
        });

        string? chassis = null;
        WmiQuery.ForEach("SELECT ChassisTypes FROM Win32_SystemEnclosure", e =>
        {
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
        });

        return new BoardInfo(maker, model, rev, serial, computerSystem.Manufacturer, computerSystem.Model, chassis);
    }

    public static FirmwareInfo ReadFirmware()
    {
        string vendor = "—", version = "—";
        string? smbios = null;
        DateTime? date = null;
        WmiQuery.ForEachOrThrow("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SMBIOSMajorVersion, SMBIOSMinorVersion FROM Win32_BIOS", b =>
        {
            vendor = Fmt.Clean(WmiQuery.GetString(b, "Manufacturer"));
            version = Fmt.Clean(WmiQuery.GetString(b, "SMBIOSBIOSVersion"));
            if (WmiCalendarDate.TryParse(b["ReleaseDate"] as string, out var parsed)) date = parsed;
            if (b["SMBIOSMajorVersion"] != null) smbios = $"{WmiQuery.GetInt32(b, "SMBIOSMajorVersion")}.{WmiQuery.GetInt32(b, "SMBIOSMinorVersion")}";
        });

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

        // TPM: precisa de administrador. SpecVersion = "2.0, 0, 1.59".
        string? tpm = null, tpmMaker = null, tpmFirmware = null;
        bool? ready = null;
        try
        {
            WmiQuery.ForEachOrThrow("SELECT SpecVersion, IsEnabled_InitialValue, IsActivated_InitialValue, ManufacturerIdTxt, ManufacturerVersion FROM Win32_Tpm", t =>
            {
                tpm = WmiQuery.GetString(t, "SpecVersion")?.Split(',')[0].Trim();
                ready = WmiQuery.GetNullableBool(t, "IsEnabled_InitialValue") == true
                    && (WmiQuery.GetNullableBool(t, "IsActivated_InitialValue") is not bool a || a);
                tpmMaker = WmiQuery.GetString(t, "ManufacturerIdTxt") is { Length: > 0 } mk ? mk : null;
                tpmFirmware = WmiQuery.GetString(t, "ManufacturerVersion") is { Length: > 0 } fw ? fw : null;
            }, @"root\CIMV2\Security\MicrosoftTpm");
            // A consulta respondeu sem nenhum TPM: ausente de fato (sem administrador ela falha e fica "não lido").
            tpm ??= "Ausente";
        }
        catch { }

        return new FirmwareInfo(vendor, version, date, smbios, uefi, secure, tpm, ready, tpmMaker, tpmFirmware);
    }

    public static List<CpuInfo> ReadCpus(bool hypervisor)
    {
        var topo = CpuTopology.Read();
        string? microcode = ReadMicrocode();
        var instructions = ReadInstructions();
        return WmiQuery.SelectOrThrow("SELECT * FROM Win32_Processor", c =>
        {
            string rawVendor = WmiQuery.GetText(c, "Manufacturer");
            string vendor = rawVendor switch
            {
                "AuthenticAMD" => "AMD",
                "GenuineIntel" => "Intel",
                var v => Fmt.ShortMaker(v)
            };
            var fm = FamilyModel.Match(WmiQuery.GetString(c, "Caption") ?? "");
            string arch = WmiQuery.GetInt32(c, "Architecture") switch { 9 => "x64", 12 => "ARM64", 0 => "x86", 5 => "ARM", _ => "—" };
            bool? virt = WmiQuery.GetNullableBool(c, "VirtualizationFirmwareEnabled");
            int cores = WmiQuery.GetInt32(c, "NumberOfCores");
            int threads = WmiQuery.GetInt32(c, "NumberOfLogicalProcessors");
            string name = MultiSpace.Replace(WmiQuery.GetText(c, "Name"), " ");
            return new CpuInfo(
                name, vendor, Fmt.Clean(WmiQuery.GetString(c, "SocketDesignation")), cores, threads,
                WmiQuery.GetInt32(c, "MaxClockSpeed"),
                fm.Success ? int.Parse(fm.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                fm.Success ? int.Parse(fm.Groups[2].Value, CultureInfo.InvariantCulture) : null,
                fm.Success ? int.Parse(fm.Groups[3].Value, CultureInfo.InvariantCulture) : null,
                arch, virt, hypervisor,
                WmiQuery.GetInt32(c, "L2CacheSize"),
                WmiQuery.GetInt32(c, "L3CacheSize"),
                // A topologia do Windows cobre todos os soquetes; com um processador só ela é dele.
                topo.Cores.Count == cores ? topo : new CpuTopology(),
                instructions, microcode, WmiQuery.GetInt32(c, "ExtClock"));
        });
    }

    public static RamInfo ReadRam(ComputerSystemSnapshot computerSystem)
    {
        var mods = new List<RamModule>();
        int partSpeed = 0;
        WmiQuery.ForEachOrThrow("SELECT * FROM Win32_PhysicalMemory", m =>
        {
            string bank = WmiQuery.GetText(m, "BankLabel");
            string loc = WmiQuery.GetText(m, "DeviceLocator");
            var (ch, idx) = ParseSlot(loc, bank);
            string part = WmiQuery.GetText(m, "PartNumber");
            if (SpeedInPart.Match(part) is { Success: true } sp)
                partSpeed = Math.Max(partSpeed, int.Parse(sp.Value, CultureInfo.InvariantCulture));
            mods.Add(new RamModule(
                bank, loc, ch != null && idx != null ? $"{ch}{idx}" : loc, ch, idx,
                WmiQuery.GetUInt64(m, "Capacity"),
                RamType(WmiQuery.GetInt32(m, "SMBIOSMemoryType"), WmiQuery.GetInt32(m, "MemoryType")),
                RamForm(WmiQuery.GetInt32(m, "FormFactor")),
                WmiQuery.GetInt32(m, "Speed"),
                WmiQuery.GetInt32(m, "ConfiguredClockSpeed"),
                Fmt.ShortMaker(Fmt.Clean(WmiQuery.GetString(m, "Manufacturer"))),
                Fmt.Clean(part),
                Fmt.Clean(WmiQuery.GetString(m, "SerialNumber")),
                WmiQuery.GetInt32(m, "DataWidth"),
                WmiQuery.GetInt32(m, "TotalWidth"),
                WmiQuery.GetInt32(m, "Attributes") & 0xF,
                Volts(m["ConfiguredVoltage"]),
                Volts(m["MinVoltage"]),
                Volts(m["MaxVoltage"]),
                WmiQuery.GetInt32(m, "TypeDetail")));
        });

        int slots = 0;
        ulong maxBytes = 0;
        WmiQuery.ForEach("SELECT MemoryDevices, MaxCapacity, MaxCapacityEx FROM Win32_PhysicalMemoryArray", a =>
        {
            slots += WmiQuery.GetInt32(a, "MemoryDevices");
            ulong kb = a["MaxCapacityEx"] != null ? WmiQuery.GetUInt64(a, "MaxCapacityEx") : WmiQuery.GetUInt64(a, "MaxCapacity");
            maxBytes += kb * 1024;
        });

        ulong virt = MemoryInfo.ReadSnapshot() is { CommitLimit: > 0 } snap ? (ulong)snap.CommitLimit : 0;

        var pages = WmiQuery.Select("SELECT Name, AllocatedBaseSize, CurrentUsage, PeakUsage FROM Win32_PageFileUsage", pf =>
            new PageFile(
                WmiQuery.GetText(pf, "Name"),
                WmiQuery.GetInt32(pf, "AllocatedBaseSize"),
                WmiQuery.GetInt32(pf, "CurrentUsage"),
                WmiQuery.GetInt32(pf, "PeakUsage")));

        return new RamInfo
        {
            Modules = mods,
            Slots = slots,
            MaxCapacity = maxBytes,
            VirtualTotal = virt,
            PartSpeed = partSpeed,
            PageFiles = pages,
            PageFileAuto = computerSystem.AutomaticManagedPagefile
        };
    }

    public static List<VideoAdapter> ReadGpus()
    {
        var vram = GpuRegistryReader.ReadVramByAdapterName();
        var list = new List<VideoAdapter>();
        WmiQuery.ForEachOrThrow("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController", g =>
        {
            string name = WmiQuery.GetText(g, "Name");
            string win = WmiQuery.GetText(g, "DriverVersion");
            string vendorRaw = WmiQuery.GetText(g, "AdapterCompatibility");
            string pnp = WmiQuery.GetString(g, "PNPDeviceID") ?? "";
            // Só o adaptador virtual do Windows sai da lista; uma placa real rodando no driver básico continua.
            if (pnp.StartsWith("ROOT", StringComparison.OrdinalIgnoreCase) && name.Contains("Basic", StringComparison.OrdinalIgnoreCase)) return;

            string vendor = vendorRaw.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA" : Fmt.ShortMaker(vendorRaw);
            DateTime? date = WmiCalendarDate.TryParse(g["DriverDate"] as string, out var parsed) ? parsed : null;
            string displayVersion = vendor == "NVIDIA" ? (HardwareNaming.NvidiaDriverVersion(win) ?? win) : win;
            list.Add(new VideoAdapter(name, vendor, displayVersion, win, date, vram.TryGetValue(name, out var v) ? v : null));
        });
        return list.OrderByDescending(g => g.Vram ?? 0).ToList();
    }

    public static List<PhysicalDiskInfo> ReadDisks()
    {
        var rpmByIndex = new Dictionary<int, int?>();
        WmiQuery.ForEach("SELECT DeviceId, SpindleSpeed FROM MSFT_PhysicalDisk", d =>
        {
            if (int.TryParse(WmiQuery.GetString(d, "DeviceId"), out int idx))
            {
                long rpm = (long)WmiQuery.GetUInt64(d, "SpindleSpeed");
                rpmByIndex[idx] = rpm is > 0 and < 100000 ? (int)rpm : null;
            }
        }, WmiDiskTopology.StorageScope);

        var meta = WmiDiskTopology.ReadPhysicalMetadata();
        var layout = new Dictionary<int, (int Logical, int Physical, int Parts, bool System)>();
        WmiQuery.ForEach("SELECT Number, LogicalSectorSize, PhysicalSectorSize, NumberOfPartitions, IsSystem, IsBoot FROM MSFT_Disk", d =>
        {
            if (WmiQuery.GetNullableInt32(d, "Number") is int n)
                layout[n] = (
                    WmiQuery.GetInt32(d, "LogicalSectorSize"),
                    WmiQuery.GetInt32(d, "PhysicalSectorSize"),
                    WmiQuery.GetInt32(d, "NumberOfPartitions"),
                    WmiQuery.GetNullableBool(d, "IsSystem") == true || WmiQuery.GetNullableBool(d, "IsBoot") == true);
        }, WmiDiskTopology.StorageScope);

        var list = new List<PhysicalDiskInfo>();
        WmiQuery.ForEachOrThrow("SELECT Index, Model, Size, InterfaceType, SerialNumber, FirmwareRevision, PNPDeviceID, MediaType FROM Win32_DiskDrive", d =>
        {
            int idx = WmiQuery.GetInt32(d, "Index");
            string pnp = WmiQuery.GetString(d, "PNPDeviceID") ?? "";
            bool usb = pnp.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase);
            meta.TryGetValue(idx, out var m);
            layout.TryGetValue(idx, out var lay);
            string bus = m?.Bus is { Length: > 0 } b ? b : usb ? "USB" : (WmiQuery.GetText(d, "InterfaceType"));
            string mediaTypeRaw = WmiQuery.GetString(d, "MediaType") ?? "";
            bool removable = usb || bus == "USB" || mediaTypeRaw.Contains("Removable", StringComparison.OrdinalIgnoreCase);
            list.Add(new PhysicalDiskInfo(
                idx,
                WmiQuery.GetText(d, "Model"),
                WmiQuery.GetUInt64(d, "Size"),
                bus,
                m?.Media ?? "",
                Fmt.Clean(WmiQuery.GetString(d, "FirmwareRevision")),
                Fmt.Clean(WmiQuery.GetString(d, "SerialNumber")),
                removable,
                m != null ? WmiDiskTopology.MapHealthStatusText(m.HealthStatus) : null,
                m?.PartitionStyle is { Length: > 0 } ps ? ps : null,
                rpmByIndex.GetValueOrDefault(idx),
                lay.Logical, lay.Physical, lay.Parts, lay.System));
        });
        return list.OrderBy(d => d.Index).ToList();
    }

    /// <summary>Revisão do microcódigo carregado em "Update Revision": Intel nos 4 últimos bytes; AMD nos 4 primeiros.</summary>
    private static string? ReadMicrocode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("Update Revision") is byte[] { Length: >= 4 } b)
            {
                uint high = b.Length >= 8 ? BitConverter.ToUInt32(b, 4) : 0;
                uint low = BitConverter.ToUInt32(b, 0);
                if ((high != 0 ? high : low) is uint rev and > 0) return $"0x{rev:X8}";
            }
        }
        catch { }
        return null;
    }

    private static List<(string, bool)> ReadInstructions()
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

    private static (string? Channel, int? Index) ParseSlot(string locator, string bank)
    {
        foreach (string text in new[] { locator, $"{bank} {locator}" })
        {
            var m = SlotName.Match(text);
            if (!m.Success) continue;
            for (int g = 1; g <= 5; g += 2)
                if (m.Groups[g].Success)
                {
                    string ch = m.Groups[g].Value.ToUpperInvariant();
                    int? idx = m.Groups[g + 1].Success ? int.Parse(m.Groups[g + 1].Value, CultureInfo.InvariantCulture) : null;
                    if (idx == null && DimmNumber.Match(locator) is { Success: true } d)
                        idx = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
                    return (ch, idx);
                }
        }
        return (null, null);
    }

    private static double? Volts(object? mv) => mv == null || Convert.ToDouble(mv, CultureInfo.InvariantCulture) <= 0
        ? null : Convert.ToDouble(mv, CultureInfo.InvariantCulture) / 1000;
}
