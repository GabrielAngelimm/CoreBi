using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Storage;

internal sealed record WmiDiskRecord(
    int Index,
    string DeviceId,
    string Model,
    string Serial,
    string Firmware,
    string Bus,
    long SizeBytes,
    string PnpDeviceId);

internal sealed record StoragePhysicalInfo(int Index, string Media, string? Bus, int HealthStatus, string? PartitionStyle);

internal static class WmiDiskTopology
{
    internal const string StorageScope = @"\\.\root\Microsoft\Windows\Storage";

    private static readonly Regex RefIdRegex = new("DeviceID=\"(.*)\"", RegexOptions.Compiled);
    private static readonly Regex PhysicalDriveRegex = new(@"PHYSICALDRIVE(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Base: Win32_DiskDrive existe em qualquer Windows (única consulta obrigatória). Sem a API de armazenamento
    // (MSFT_*) ou sem permissão, a leitura segue só com o resto.
    public static List<WmiDiskRecord> ReadDiskDrivesOrThrow() =>
        WmiQuery.SelectOrThrow(
            "SELECT Index, DeviceID, Model, SerialNumber, FirmwareRevision, InterfaceType, Size, PNPDeviceID FROM Win32_DiskDrive",
            d =>
            {
                int idx = WmiQuery.GetInt32(d, "Index");
                string pnp = WmiQuery.GetString(d, "PNPDeviceID") ?? "";
                string bus = pnp.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)
                    ? "USB"
                    : (WmiQuery.GetString(d, "InterfaceType") ?? "");
                return new WmiDiskRecord(
                    idx,
                    WmiQuery.GetString(d, "DeviceID") ?? $@"\\.\PHYSICALDRIVE{idx}",
                    WmiQuery.GetString(d, "Model") ?? $"Disco {idx}",
                    WmiQuery.GetString(d, "SerialNumber") ?? "",
                    WmiQuery.GetString(d, "FirmwareRevision") ?? "",
                    bus,
                    (long)WmiQuery.GetUInt64(d, "Size"),
                    pnp);
            });

    public static Dictionary<int, StoragePhysicalInfo> ReadPhysicalMetadata()
    {
        var styles = new Dictionary<int, string>();
        WmiQuery.ForEach("SELECT Number, PartitionStyle FROM MSFT_Disk", d =>
        {
            if (WmiQuery.GetNullableInt32(d, "Number") is int num)
                styles[num] = MapPartitionStyle(WmiQuery.GetInt32(d, "PartitionStyle"));
        }, StorageScope);

        var map = new Dictionary<int, StoragePhysicalInfo>();
        WmiQuery.ForEach("SELECT DeviceId, HealthStatus, MediaType, BusType FROM MSFT_PhysicalDisk", d =>
        {
            string devId = WmiQuery.GetString(d, "DeviceId") ?? "";
            if (!int.TryParse(devId, out int idx)) return;
            string media = MapMediaType(WmiQuery.GetInt32(d, "MediaType"));
            string? bus = MapBusType(WmiQuery.GetInt32(d, "BusType"));
            int health = WmiQuery.GetNullableInt32(d, "HealthStatus") ?? 5;
            styles.TryGetValue(idx, out string? style);
            map[idx] = new StoragePhysicalInfo(idx, media, bus, health, style);
        }, StorageScope);

        return map;
    }

    public static string MapMediaType(int code) => code switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => ""
    };

    public static string? MapBusType(int code) => code switch
    {
        17 => "NVMe",
        11 => "SATA",
        7 => "USB",
        10 => "SAS",
        8 => "RAID",
        12 => "SD",
        6 => "Fibre Channel",
        3 => "ATA",
        _ => null
    };

    public static string MapPartitionStyle(int code) => code switch
    {
        1 => "MBR",
        2 => "GPT",
        _ => ""
    };

    public static string? MapHealthStatusText(int code) => code switch
    {
        0 => "Saudável",
        1 => "Atenção",
        2 => "Não saudável",
        _ => null
    };

    /// <summary>
    /// Lê as tabelas de associação do WMI (disco→partição e partição→letra) diretamente,
    /// porque consultas "ASSOCIATORS OF {caminho}" falham em algumas máquinas.
    /// </summary>
    public static Dictionary<int, List<string>> ReadLettersByDiskIndex()
    {
        var diskOfPart = ReadPartitionToDiskMap();
        var letters = new Dictionary<int, List<string>>();
        WmiQuery.ForEach("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition", a =>
        {
            if (RefId(a["Antecedent"]) is string part && RefId(a["Dependent"]) is string letter &&
                diskOfPart.TryGetValue(part, out var disk))
            {
                var m = PhysicalDriveRegex.Match(disk);
                if (!m.Success) return;
                int idx = int.Parse(m.Groups[1].Value);
                if (!letters.TryGetValue(idx, out var list)) letters[idx] = list = new List<string>();
                if (!list.Contains(letter, StringComparer.OrdinalIgnoreCase)) list.Add(letter);
            }
        });
        foreach (var list in letters.Values) list.Sort(StringComparer.OrdinalIgnoreCase);
        return letters;
    }

    public static Dictionary<string, (string? Model, string? Bus)> ReadDisksByLetter()
    {
        var map = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        var disks = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        WmiQuery.ForEach("SELECT DeviceID, Model, InterfaceType, PNPDeviceID FROM Win32_DiskDrive", d =>
        {
            string? devId = WmiQuery.GetString(d, "DeviceID");
            if (string.IsNullOrEmpty(devId)) return;
            string? pnp = WmiQuery.GetString(d, "PNPDeviceID");
            string? bus = pnp?.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase) == true
                ? "USB"
                : WmiQuery.GetString(d, "InterfaceType");
            disks[devId] = (WmiQuery.GetString(d, "Model"), bus);
        });

        var diskOfPartition = ReadPartitionToDiskMap();
        WmiQuery.ForEach("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition", a =>
        {
            if (RefId(a["Antecedent"]) is string part && RefId(a["Dependent"]) is string letter &&
                diskOfPartition.TryGetValue(part, out var disk) && disks.TryGetValue(disk, out var info))
                map[letter] = info;
        });
        return map;
    }

    private static Dictionary<string, string> ReadPartitionToDiskMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        WmiQuery.ForEach("SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition", a =>
        {
            if (RefId(a["Antecedent"]) is string disk && RefId(a["Dependent"]) is string part)
                map[part] = disk;
        });
        return map;
    }

    /// <summary>Referência WMI → valor de DeviceID: ...Win32_DiskDrive.DeviceID="\\\\.\\PHYSICALDRIVE0" → \\.\PHYSICALDRIVE0.</summary>
    internal static string? RefId(object? reference)
    {
        var m = RefIdRegex.Match(reference?.ToString() ?? "");
        return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\") : null;
    }
}
