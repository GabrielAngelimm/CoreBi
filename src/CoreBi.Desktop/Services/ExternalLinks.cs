using System.Diagnostics;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Desktop.Services;

internal static class ExternalLinks
{
    public static void OpenHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Endereço HTTPS inválido.", nameof(url));
        try { using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { DiagnosticLog.Write("external-link", ex); }
    }
}
