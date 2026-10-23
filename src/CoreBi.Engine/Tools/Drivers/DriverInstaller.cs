using LibreHardwareMonitor.Hardware;

using System.Diagnostics;

using System.Security.Cryptography.X509Certificates;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Drivers;

internal static class DriverInstaller
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromMinutes(30) };

    public static async Task<string> DownloadAndLaunchAsync(GpuDriverStatus driver, Action<string, double> progress, CancellationToken token)
    {
        Uri uri = DriverDownloadPolicy.Validate(driver.DownloadUrl, driver.Vendor);
        token.ThrowIfCancellationRequested();
        string folder = Path.Combine(ProtectedWorkspace.GetFolder("Downloads"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "driver.exe");
        bool launched = false;
        try
        {
            using (var response = await DownloadAsync(uri, driver.Vendor, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;
                const long maxBytes = 4L << 30;
                if (total > maxBytes) throw new IOException("Instalador excede o limite de 4 GiB.");
                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var target = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                var buffer = new byte[1 << 20]; long done = 0; int count;
                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    done += count;
                    if (done > maxBytes) throw new IOException("Instalador excede o limite de 4 GiB.");
                    await target.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    progress($"Baixando… {done / 1048576.0:F0} MiB", total > 0 ? (double)done / total : 0);
                }
                if (total > 0 && done != total) throw new IOException("Download incompleto.");
            }
            // Keep the verified file locked against replacement until CreateProcess has mapped the image.
            using var guard = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            progress("Conferindo a assinatura digital…", 1);
            VerifyPublisher(file, driver.Vendor);
            token.ThrowIfCancellationRequested();
            string restore = await TryRestorePointAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo(file) { UseShellExecute = false, WorkingDirectory = folder })
                ?? throw new InvalidOperationException("Não foi possível iniciar o instalador.");
            launched = true;
            return $"Instalador {driver.Latest} aberto (assinatura {driver.Vendor} válida). {restore} Siga o instalador oficial.";
        }
        finally
        {
            if (!launched)
            {
                try { File.Delete(file); Directory.Delete(folder); }
                catch (Exception ex) { DiagnosticLog.Write("driver.cleanup", ex); }
            }
        }
    }

    private static async Task<HttpResponseMessage> DownloadAsync(Uri uri, string vendor, CancellationToken token)
    {
        for (int redirects = 0; redirects < 6; redirects++)
        {
            DriverDownloadPolicy.Validate(uri.AbsoluteUri, vendor);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (vendor == "AMD") request.Headers.Referrer = new Uri("https://www.amd.com/");
            var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                Uri? next = response.Headers.Location;
                response.Dispose();
                if (next == null) throw new HttpRequestException("Redirecionamento sem destino.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }
            return response;
        }
        throw new HttpRequestException("Limite de redirecionamentos excedido.");
    }

    private static void VerifyPublisher(string file, string vendor)
    {
        int trust = AuthenticodeVerifier.WinVerifyTrustFile(file);
        if (trust != 0) throw new InvalidOperationException($"Assinatura digital inválida (0x{trust:X8}).");
#pragma warning disable SYSLIB0057
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
        if (!DriverDownloadPolicy.IsPublisher(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false), vendor))
            throw new InvalidOperationException("O assinante não corresponde ao fabricante esperado.");
    }

    private static async Task<string> TryRestorePointAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var result = await ProcessRunner.RunAsync(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                new[] { "-NoProfile", "-NonInteractive", "-Command", "Checkpoint-Computer -Description 'CoreBi - antes do driver' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop" }, timeout.Token).ConfigureAwait(false);
            return result.ExitCode == 0 ? "Ponto de restauração criado." : "Ponto de restauração não criado (verifique a proteção do sistema).";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { DiagnosticLog.Write("driver.restore-point", ex); return "Ponto de restauração não criado."; }
    }
}
