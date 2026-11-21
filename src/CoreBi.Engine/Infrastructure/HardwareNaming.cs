using System.Text.RegularExpressions;

namespace CoreBi.Engine.Infrastructure;

public static partial class HardwareNaming
{
    [GeneratedRegex(@"Laptop|Mobile|Max-Q|Notebook", RegexOptions.IgnoreCase)]
    private static partial Regex MobileGpuRegex();

    [GeneratedRegex(@"\d+th Gen\s*")]
    private static partial Regex IntelGenRegex();

    internal static bool IsMobileGpu(string? name) =>
        !string.IsNullOrWhiteSpace(name) && MobileGpuRegex().IsMatch(name);

    internal static bool IsMicrosoftBasicAdapter(string? name, string? pnpDeviceId = null) =>
        (name?.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ?? false)
        || (pnpDeviceId?.StartsWith("ROOT", StringComparison.OrdinalIgnoreCase) == true
            && (name?.Contains("Basic", StringComparison.OrdinalIgnoreCase) ?? false));

    public static string ShortGpuName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string s = name.Replace("(TM)", "").Replace("(R)", "");
        foreach (string w in new[] { "NVIDIA", "GeForce", "AMD", "Intel" }) s = s.Replace(w, "");
        if (s.Contains("Radeon RX")) s = s.Replace("Radeon", "");
        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (s.StartsWith("Arc") && s.EndsWith(" Graphics")) s = s[..^9];
        return s;
    }

    public static string ShortCpuName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string s = name.Replace("AMD", "").Replace("Intel(R)", "").Replace("(R)", "").Replace("(TM)", "");
        int i = s.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
        if (i > 0)
        {
            int space = s.LastIndexOf(' ', i);
            s = space >= 0 ? s[..space] : s[..i];
        }
        s = s.Replace("Processor", "").Replace("CPU", "");
        s = IntelGenRegex().Replace(s, "");
        int at = s.IndexOf('@');
        if (at > 0) s = s[..at];
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Converte versão WMI da NVIDIA (ex.: "32.0.15.9597") para o formato de pacote ("595.97").</summary>
    internal static string? NvidiaDriverVersion(string? wmiVersion)
    {
        if (string.IsNullOrWhiteSpace(wmiVersion)) return null;
        string[] parts = wmiVersion.Split('.');
        if (parts.Length < 4) return null;
        string digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5) return null;
        string tail = digits[^5..];
        return int.TryParse(tail[..3], out int major) ? $"{major}.{tail[3..]}" : null;
    }

    public static string Sentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string trimmed = text.Trim();
        return char.IsLetter(trimmed[0])
            ? char.ToUpper(trimmed[0], ByteFormat.PtBr) + trimmed[1..]
            : trimmed;
    }
}
