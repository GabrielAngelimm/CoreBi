namespace CoreBi.Engine.Tools.Drivers;

internal static class DriverDownloadPolicy
{
    public static Uri Validate(string url, string vendor)
    {
        string domain = vendor switch { "NVIDIA" => "nvidia.com", "AMD" => "amd.com", _ => throw new ArgumentException("Fabricante não suportado.") };
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort
            || !(uri.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase)
                 || uri.IdnHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("O download deve usar HTTPS em um domínio oficial do fabricante.");
        return uri;
    }

    public static bool IsPublisher(string simpleName, string vendor)
    {
        static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        return (Normalize(simpleName), vendor) switch
        {
            ("NVIDIACORPORATION", "NVIDIA") => true,
            ("ADVANCEDMICRODEVICESINC", "AMD") => true,
            _ => false
        };
    }
}
