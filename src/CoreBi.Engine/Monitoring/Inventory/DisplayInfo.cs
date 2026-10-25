using System.Management;
using System.Runtime.InteropServices;

namespace CoreBi.Engine.Monitoring.Inventory;

/// <summary>Uma tela ligada à área de trabalho: modo atual, a maior taxa que ela aceita nessa resolução e o monitor.</summary>
public sealed record DisplayInfo(
    int Number, string Adapter, string Monitor, string? Maker, double? Inches,
    int Width, int Height, int Hz, int MaxHz, int Bits, int X, int Y, bool Primary,
    string? Connection = null, int? Year = null, int? Week = null)
{
    /// <summary>Roda abaixo do que o monitor aceita na mesma resolução (o caso típico: 144 Hz ligado a 60 Hz).</summary>
    public bool BelowMax => MaxHz > Hz + 1 && Hz > 0;
}

public static class DisplayReader
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

    private const int AttachedToDesktop = 0x1, PrimaryDevice = 0x4, CurrentSettings = -1;
    private const uint GetDeviceInterfaceName = 0x1;

    public static List<DisplayInfo> Read()
    {
        var names = MonitorNames();
        var list = new List<DisplayInfo>();
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++, dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>())
        {
            if ((dd.StateFlags & AttachedToDesktop) == 0) continue;
            string device = dd.DeviceName, adapter = dd.DeviceString.Trim();
            bool primary = (dd.StateFlags & PrimaryDevice) != 0;
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(device, CurrentSettings, ref dm)) continue;

            // Maior taxa que o driver oferece na resolução atual.
            int max = dm.dmDisplayFrequency;
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            for (int m = 0; EnumDisplaySettings(device, m, ref mode); m++)
                if (mode.dmPelsWidth == dm.dmPelsWidth && mode.dmPelsHeight == dm.dmPelsHeight && mode.dmBitsPerPel == dm.dmBitsPerPel)
                    max = Math.Max(max, mode.dmDisplayFrequency);

            // Monitor: o caminho da interface (\\?\DISPLAY#GSM5B7F#5&...#{guid}) casa com a instância do WMI.
            string monitor = "Monitor", key = "";
            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(device, 0, ref mon, GetDeviceInterfaceName))
            {
                monitor = mon.DeviceString.Trim();
                var parts = mon.DeviceID.Split('#');
                if (parts.Length >= 3) key = $@"DISPLAY\{parts[1]}\{parts[2]}";
            }
            var known = names.FirstOrDefault(n => key.Length > 0 && n.Instance.StartsWith(key, StringComparison.OrdinalIgnoreCase));
            int number = int.TryParse(new string(device.Where(char.IsDigit).ToArray()), out int n) ? n : list.Count + 1;
            list.Add(new DisplayInfo(number, adapter, known.Name ?? monitor, known.Maker, known.Inches,
                dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency, max, dm.dmBitsPerPel, dm.dmPositionX, dm.dmPositionY, primary,
                known.Connection, known.Year, known.Week));
        }
        return list.OrderByDescending(d => d.Primary).ThenBy(d => d.Number).ToList();
    }

    /// <summary>Nome comercial (EDID), tamanho da tela, cabo e data de fabricação, por instância do monitor.</summary>
    private static List<(string Instance, string? Name, string? Maker, double? Inches, string? Connection, int? Year, int? Week)> MonitorNames()
    {
        var list = new List<(string, string?, string?, double?, string?, int?, int?)>();
        try
        {
            var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var c = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams");
                foreach (ManagementObject m in c.Get())
                    if (Convert.ToInt64(m["VideoOutputTechnology"] ?? -2L) switch
                    {
                        0 => "VGA",
                        4 => "DVI",
                        5 => "HDMI",
                        10 => "DisplayPort",
                        11 => "DisplayPort interna",
                        15 => "Miracast (sem fio)",
                        0x80000000 => "Tela interna",
                        _ => null
                    } is string link)
                        links[m["InstanceName"]?.ToString() ?? ""] = link;
            }
            catch { }
            var sizes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams"))
                foreach (ManagementObject m in s.Get())
                {
                    double w = Convert.ToDouble(m["MaxHorizontalImageSize"] ?? 0), h = Convert.ToDouble(m["MaxVerticalImageSize"] ?? 0);
                    if (w > 0 && h > 0) sizes[m["InstanceName"]?.ToString() ?? ""] = Math.Sqrt(w * w + h * h) / 2.54;
                }
            using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, UserFriendlyName, ManufacturerName, YearOfManufacture, WeekOfManufacture FROM WmiMonitorID"))
                foreach (ManagementObject m in s.Get())
                {
                    string inst = m["InstanceName"]?.ToString() ?? "";
                    int year = Convert.ToInt32(m["YearOfManufacture"] ?? 0), week = Convert.ToInt32(m["WeekOfManufacture"] ?? 0);
                    list.Add((inst, Edid(m["UserFriendlyName"]), Edid(m["ManufacturerName"]), sizes.TryGetValue(inst, out var d) ? Math.Round(d) : null,
                        links.GetValueOrDefault(inst), year >= 1990 ? year : null, week is >= 1 and <= 53 ? week : null));
                }
        }
        catch { }
        return list;
    }

    private static string? Edid(object? v)
    {
        if (v is not ushort[] chars) return null;
        string s = new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
        return s.Length > 0 ? s : null;
    }
}
