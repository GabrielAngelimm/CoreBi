using System.Management;
namespace CoreBi.Engine.Monitoring.Devices;

public sealed record ConnectedDevice(string InstanceId, string Name, string? Maker)
{
    public bool Present => true;
}

/// <summary>Read-only USB inventory for connection notifications. No removal, COM or driver operations.</summary>
public static class UsbDeviceInventory
{
    public static List<ConnectedDevice> List()
    {
        using var query = new ManagementObjectSearcher("SELECT PNPDeviceID, Name, Manufacturer FROM Win32_PnPEntity WHERE Present=True");
        var devices = new List<ConnectedDevice>();
        foreach (ManagementObject item in query.Get())
        {
            string id = item["PNPDeviceID"]?.ToString() ?? "";
            if (!id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) && !id.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase)) continue;
            devices.Add(new(id, item["Name"]?.ToString() ?? "Dispositivo USB", item["Manufacturer"]?.ToString()));
        }
        return devices;
    }
}
