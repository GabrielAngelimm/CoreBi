using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Drivers;

/// <summary>Exporta todos os drivers de terceiros via pnputil para reinstalação pós-formatação.</summary>
public static class DriverBackup
{
    public static async Task<string> ExportAsync(Action<string> progress, CancellationToken token)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CoreBi-Drivers", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        progress("Exportando drivers…");
        var result = await ProcessRunner.RunAsync(Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            new[] { "/export-driver", "*", folder }, token).ConfigureAwait(false);
        int count = Directory.GetDirectories(folder).Length;
        return result.ExitCode == 0
            ? $"Exportação concluída: {count} pastas em {folder}"
            : $"Exportação incompleta (código 0x{result.ExitCode:X8}). {count} pastas em {folder}.";
    }
}
