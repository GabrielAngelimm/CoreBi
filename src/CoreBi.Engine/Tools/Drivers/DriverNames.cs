using System.Text;
using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Drivers;

/// <summary>
/// Nomes de driver em português claro, para a tela. O Windows fala em arquivos e classes ("nvhdci.inf", "MEDIA", "Realtek
/// Semiconductor Corp. - Extension - 10.0.1"); aqui eles viram "driver de som da NVIDIA" ou "Realtek · Extensão".
/// </summary>
public static partial class DriverNames
{
    /// <summary>A classe do Windows em português ("MEDIA" → "som"); vazio quando é uma classe sem nome conhecido.</summary>
    public static string Kind(string windowsClass) => windowsClass.Trim().ToUpperInvariant() switch
    {
        "DISPLAY" => "vídeo",
        "MEDIA" or "AUDIOPROCESSINGOBJECT" or "AUDIOENDPOINT" => "som",
        "NET" or "NETSERVICE" or "NETTRANS" or "NETCLIENT" => "rede",
        "BLUETOOTH" => "Bluetooth",
        "SCSIADAPTER" or "HDC" or "DISKDRIVE" => "armazenamento",
        "USB" or "USBDEVICE" => "USB",
        "SYSTEM" => "sistema",
        "HIDCLASS" => "controle e entrada",
        "KEYBOARD" => "teclado",
        "MOUSE" => "mouse",
        "MONITOR" => "monitor",
        "CAMERA" or "IMAGE" => "câmera",
        "PRINTER" or "PRINTQUEUE" => "impressora",
        "EXTENSION" => "extensão",
        "SOFTWARECOMPONENT" => "componente",
        "SOFTWAREDEVICE" => "dispositivo de software",
        "FIRMWARE" => "firmware",
        "BIOMETRIC" => "biometria",
        "SECURITYDEVICES" => "segurança",
        "SENSOR" => "sensor",
        "PORTS" => "portas",
        _ => "",
    };

    /// <summary>O fabricante sem a razão social ("Realtek Semiconductor Corp." → "Realtek Semiconductor").</summary>
    public static string Maker(string vendor) => HardwareNaming.CompanyName(vendor);

    /// <summary>
    /// Um driver do Windows Update ("Realtek Semiconductor Corp. - Extension - 10.0.26100.31") em fabricante, tipo e versão. Título
    /// fora desse formato fica inteiro no fabricante.
    /// </summary>
    public static (string Maker, string Kind, string Version) WindowsUpdate(string title)
    {
        string[] parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !VersionLike().IsMatch(parts[^1])) return (title.Trim(), "", "");
        string kind = Kind(parts[1]);
        if (kind.Length == 0) kind = parts[1];
        else kind = char.ToUpper(kind[0], System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + kind[1..];
        return (Maker(string.Join(" - ", parts[..^2])), kind, parts[^1]);
    }

    [GeneratedRegex(@"^\d+(\.\d+)+$")]
    private static partial Regex VersionLike();

    /// <summary>
    /// O pacote de um backup em palavras, pelo [Version] do próprio .inf: "driver de som da NVIDIA". Sem fabricante legível, fica o
    /// nome do arquivo ("nvhdci.inf"). Lê só o começo do arquivo (os .inf grandes têm milhares de linhas de modelos).
    /// </summary>
    public static string Package(string infPath)
    {
        string file = Path.GetFileName(infPath);
        try
        {
            var (cls, provider) = ReadVersion(infPath);
            provider = Maker(provider);
            if (provider.Length == 0 || provider.StartsWith('%')) return file;
            string kind = Kind(cls);
            return kind.Length > 0 ? $"driver de {kind} da {provider}" : $"driver da {provider}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return file; }
    }

    /// <summary>Class e Provider do [Version], com os %nomes% trocados pelo texto da primeira seção [Strings].</summary>
    internal static (string Class, string Provider) ReadVersion(string infPath)
    {
        // Os .inf vêm em ANSI ou UTF-16 com BOM; o leitor detecta pelo BOM
        using var reader = new StreamReader(infPath, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        string section = "", cls = "", provider = "";
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool stringsSeen = false, inStrings = false;
        int lines = 0;
        for (string? line = reader.ReadLine(); line != null && lines < 200_000; line = reader.ReadLine(), lines++)
        {
            string t = line.Trim();
            int semi = t.IndexOf(';');
            if (semi == 0) continue;
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                section = t[1..^1].Trim();
                bool isStrings = section.Equals("Strings", StringComparison.OrdinalIgnoreCase);
                if (inStrings && !isStrings) break;
                inStrings = isStrings && !stringsSeen;
                if (isStrings) stringsSeen = true;
                continue;
            }
            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            string key = t[..eq].Trim(), value = Unquote(t[(eq + 1)..]);
            if (section.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("Class", StringComparison.OrdinalIgnoreCase)) cls = value;
                else if (key.Equals("Provider", StringComparison.OrdinalIgnoreCase)) provider = value;
            }
            else if (inStrings) strings.TryAdd(key, value);
        }
        return (Resolve(cls, strings), Resolve(provider, strings));
    }

    private static string Unquote(string raw)
    {
        string v = raw.Trim();
        if (v.StartsWith('"'))
        {
            int end = v.IndexOf('"', 1);
            return end > 0 ? v[1..end] : v.Trim('"');
        }
        int semi = v.IndexOf(';');
        return (semi >= 0 ? v[..semi] : v).Trim();
    }

    private static string Resolve(string value, Dictionary<string, string> strings) =>
        value.Length > 2 && value.StartsWith('%') && value.EndsWith('%') && strings.TryGetValue(value[1..^1], out var s) ? s : value;
}
