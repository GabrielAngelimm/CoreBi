using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Devices;

public sealed record ConnectedDevice(string InstanceId, string Name, string? Maker);

/// <summary>Read-only USB inventory for connection notifications. No removal, COM or driver operations.</summary>
public static class UsbDeviceInventory
{
    private const string Query =
        "SELECT PNPDeviceID, Name, Manufacturer FROM Win32_PnPEntity " +
        "WHERE Present=True AND (PNPDeviceID LIKE 'USB\\\\%' OR PNPDeviceID LIKE 'USBSTOR\\\\%')";

    public static List<ConnectedDevice> List() =>
        WmiQuery.SelectOrThrow(Query, item => new ConnectedDevice(
            WmiQuery.GetString(item, "PNPDeviceID") ?? "",
            WmiQuery.GetString(item, "Name") ?? "Dispositivo USB",
            WmiQuery.GetString(item, "Manufacturer")));
}
