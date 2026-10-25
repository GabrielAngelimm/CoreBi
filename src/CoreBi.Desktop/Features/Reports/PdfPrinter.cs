using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CoreBi.Desktop.Features.Reports;

/// <summary>
/// Gera o PDF do relatório com o Microsoft Edge em modo headless (sem janela), o mesmo motor que imprime o HTML.
/// O Edge vem com o Windows 10 e 11, então não há biblioteca nova no aplicativo: o PDF sai igual à impressão do navegador,
/// em A4 com cabeçalho, rodapé e "Página X de Y".
/// </summary>
internal static class PdfPrinter
{
    public static string? FindEdge()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
                if (key?.GetValue(null) is string path && File.Exists(path)) return path;
            }
            catch { }
        }
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string path = Path.Combine(Environment.GetFolderPath(root), @"Microsoft\Edge\Application\msedge.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Escreve o HTML num arquivo temporário e imprime em <paramref name="pdfPath"/>. Lança exceção com o motivo se falhar.</summary>
    public static async Task PrintAsync(string html, string pdfPath, CancellationToken cancel = default)
    {
        string edge = FindEdge() ?? throw new InvalidOperationException("O Microsoft Edge não foi encontrado. Exporte em HTML e use Imprimir > Salvar como PDF.");
        string work = Path.Combine(Path.GetTempPath(), "CoreBi-pdf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        string source = Path.Combine(work, "relatorio.html"), output = Path.Combine(work, "relatorio.pdf");
        try
        {
            await File.WriteAllTextAsync(source, html, new UTF8Encoding(true), cancel);
            // Perfil próprio e temporário: não mexe no Edge aberto nem no perfil de quem usa o PC.
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
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (cancel.IsCancellationRequested) throw;
            }
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("O Microsoft Edge não gerou o PDF. Exporte em HTML e use Imprimir > Salvar como PDF.");
            File.Copy(output, pdfPath, overwrite: true);
        }
        finally
        {
            // O Edge pode segurar o perfil por um instante depois de sair.
            for (int i = 0; i < 5; i++)
            {
                try { Directory.Delete(work, recursive: true); break; }
                catch { await Task.Delay(300, CancellationToken.None); }
            }
        }
    }
}
