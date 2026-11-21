using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Inventory;

public static class SystemInfoCollector
{
    internal static List<InfoNode> Collect() => Read().ToNodes();

    public static SystemInventory Read()
    {
        var inv = new SystemInventory();
        var computerSystem = OsInventoryReader.ReadComputerSystem();

        Safe(inv, SystemInventory.OsTitle, () => inv.Os = OsInventoryReader.ReadOs(computerSystem));
        Safe(inv, SystemInventory.BoardTitle, () =>
        {
            inv.Board = HardwareInventoryReader.ReadBoard(computerSystem);
            inv.Firmware = HardwareInventoryReader.ReadFirmware();
        });
        Safe(inv, SystemInventory.CpuTitle, () => inv.Cpus.AddRange(HardwareInventoryReader.ReadCpus(computerSystem.HypervisorPresent)));
        Safe(inv, SystemInventory.RamTitle, () => inv.Ram = HardwareInventoryReader.ReadRam(computerSystem));
        Safe(inv, SystemInventory.GpuTitle, () => inv.Gpus.AddRange(HardwareInventoryReader.ReadGpus()));
        Safe(inv, SystemInventory.GpuTitle, () => inv.Displays.AddRange(DisplayReader.Read()));
        Safe(inv, SystemInventory.StorageTitle, () => inv.Disks = HardwareInventoryReader.ReadDisks());
        Safe(inv, SystemInventory.StorageTitle, () => inv.BitLocker = OsInventoryReader.ReadBitLocker());
        Safe(inv, SystemInventory.OsTitle, () => inv.Settings = OsInventoryReader.ReadSettings());
        Safe(inv, SystemInventory.OsTitle, () => inv.Security = OsInventoryReader.ReadSecurity());
        return inv;
    }

    public static Dictionary<string, bool> ReadBitLocker() => OsInventoryReader.ReadBitLocker();

    public static List<PhysicalDiskInfo> ReadDisks() => HardwareInventoryReader.ReadDisks();

    public static string ReadCpuName() =>
        WmiQuery.FirstOrDefault("SELECT Name FROM Win32_Processor", mo => WmiQuery.GetString(mo, "Name")) ?? "";

    private static void Safe(SystemInventory inv, string title, Action read)
    {
        try { read(); }
        catch (Exception ex) { inv.Errors.TryAdd(title, ex.Message); }
    }
}
