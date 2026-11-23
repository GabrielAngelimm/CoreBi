using System.Diagnostics;
using System.Text;
using CoreBi.Engine.Infrastructure;
using Microsoft.Win32;

namespace CoreBi.Engine.Reports;

/// <summary>
/// Gera o PDF do relatório usando o Microsoft Edge em modo headless (o mesmo motor de impressão do HTML),
/// produzindo A4 com cabeçalho, rodapé e numeração de páginas sem dependências externas.
/// </summary>
public static class PdfPrinter
{
    /// <summary>
    /// Só o registro da máquina e as pastas de programas: o processo é iniciado com a elevação do CoreBi, e o App Paths
    /// do usuário (HKCU) qualquer programa da conta pode apontar para outro executável.
    /// </summary>
    private static string? FindEdge()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
            if (key?.GetValue(null) is string path && File.Exists(path)) return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string path = Path.Combine(Environment.GetFolderPath(root), @"Microsoft\Edge\Application\msedge.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static async Task PrintAsync(string html, string pdfPath, CancellationToken cancel = default)
    {
        string edge = FindEdge() ?? throw new InvalidOperationException("O Microsoft Edge não foi encontrado. Exporte em HTML e use Imprimir > Salvar como PDF.");
        string work = Path.Combine(Path.GetTempPath(), "CoreBi-pdf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        string source = Path.Combine(work, "relatorio.html"), output = Path.Combine(work, "relatorio.pdf");
        try
        {
            await File.WriteAllTextAsync(source, html, new UTF8Encoding(true), cancel).ConfigureAwait(false);
            // Perfil próprio e temporário (--user-data-dir): não mexe no Edge aberto nem no perfil de quem usa o PC.
            var psi = new ProcessStartInfo(edge)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            foreach (var arg in new[]
            {
                "--headless", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                "--no-pdf-header-footer", "--print-to-pdf-no-header", "--user-data-dir=" + Path.Combine(work, "perfil"),
                "--print-to-pdf=" + output, new Uri(source).AbsoluteUri,
            })
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o Microsoft Edge.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (cancel.IsCancellationRequested) throw;
            }
            // Com o app elevado o Edge se relança sem elevação e o processo filho sai na hora: o PDF surge logo depois.
            // Espera o arquivo existir e parar de crescer antes de copiar e apagar a pasta de trabalho.
            long lastSize = -1;
            // Sem elevação o Edge já terminou aqui, então 2 s bastam. Elevado, o Edge relançado ainda precisa abrir
            // (perfil novo, disco frio) e imprimir: espera o prazo inteiro, como antes.
            int maxEmptyWaits = ProcessElevation.IsElevated ? 240 : 8;
            for (int waited = 0; waited < 240; waited++)
            {
                long size = File.Exists(output) ? new FileInfo(output).Length : 0;
                if (size > 0 && size == lastSize) break;
                if (size == 0 && waited >= maxEmptyWaits) break;
                lastSize = size;
                await Task.Delay(250, cancel).ConfigureAwait(false);
            }
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("O Microsoft Edge não gerou o PDF. Exporte em HTML e use Imprimir > Salvar como PDF.");
            // A pasta de trabalho é do usuário (o Edge sem elevação escreve nela). Um atalho de sistema de arquivos no
            // lugar do PDF faria a cópia, elevada, ler outro arquivo: só copia um arquivo comum.
            if ((File.GetAttributes(work) | File.GetAttributes(output)).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("O PDF gerado foi trocado por um atalho. Exporte de novo.");
            File.Copy(output, pdfPath, overwrite: true);
        }
        finally
        {
            // O Edge pode segurar o perfil temporário por um instante depois de sair.
            for (int i = 0; i < 5; i++)
            {
                try { Directory.Delete(work, recursive: true); break; }
                catch { await Task.Delay(300, CancellationToken.None).ConfigureAwait(false); }
            }
        }
    }
}
