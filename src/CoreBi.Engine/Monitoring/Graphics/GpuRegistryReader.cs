using Microsoft.Win32;

namespace CoreBi.Engine.Monitoring.Graphics;

internal static class GpuRegistryReader
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>VRAM em 64 bits pelo registro do driver (o WMI corta em 4 GB); trata REG_QWORD, REG_DWORD e REG_BINARY.</summary>
    public static ulong ReadMemorySizeBytes(IReadOnlyDictionary<string, object> values)
    {
        if (values.TryGetValue("HardwareInformation.qwMemorySize", out var qw))
        {
            ulong bytes = qw switch
            {
                long l when l > 0 => (ulong)l,
                ulong u => u,
                byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
                _ => 0
            };
            if (bytes > 0) return bytes;
        }
        if (values.TryGetValue("HardwareInformation.MemorySize", out var mem))
        {
            return mem switch
            {
                int i when i != 0 => (uint)i, // REG_DWORD acima de 2 GB chega negativo como int
                uint u => u,
                byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                _ => 0
            };
        }
        return 0;
    }

    public static Dictionary<string, object> ReadDriverHardwareInfo(string? driverKey)
    {
        var dict = new Dictionary<string, object>();
        if (string.IsNullOrEmpty(driverKey)) return dict;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{driverKey}");
            if (key == null) return dict;
            foreach (string name in key.GetValueNames())
                if (name.StartsWith("HardwareInformation.", StringComparison.Ordinal) && key.GetValue(name) is object v)
                    dict[name] = v;
        }
        catch { }
        return dict;
    }

    public static Dictionary<string, ulong> ReadVramByAdapterName()
    {
        var map = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (cls == null) return map;
            foreach (string sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                try
                {
                    using var key = cls.OpenSubKey(sub);
                    if (key?.GetValue("DriverDesc") is not string desc) continue;
                    var values = new Dictionary<string, object>();
                    if (key.GetValue("HardwareInformation.qwMemorySize") is object qw) values["HardwareInformation.qwMemorySize"] = qw;
                    if (key.GetValue("HardwareInformation.MemorySize") is object ms) values["HardwareInformation.MemorySize"] = ms;
                    ulong bytes = ReadMemorySizeBytes(values);
                    if (bytes > 0 && (!map.TryGetValue(desc, out var old) || bytes > old)) map[desc] = bytes;
                }
                catch { }
            }
        }
        catch { }
        return map;
    }

    /// <summary>Alguns drivers gravam strings no Registro como REG_BINARY UTF-16.</summary>
    public static string? ReadString(IReadOnlyDictionary<string, object> values, string name)
    {
        if (!values.TryGetValue(name, out var v)) return null;
        string s = v is byte[] bytes ? System.Text.Encoding.Unicode.GetString(bytes) : v.ToString() ?? "";
        s = s.TrimEnd('\0').Trim();
        return s.Length > 0 ? s : null;
    }
}
