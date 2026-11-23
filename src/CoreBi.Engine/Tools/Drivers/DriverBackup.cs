using System.Text.Json;
using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Drivers;

/// <summary>Um backup de drivers numa pasta: quando, de qual PC, quantos pacotes e o tamanho.</summary>
public sealed record DriverBackupInfo(string Folder, DateTime Created, string Computer, int Count, long Bytes);

/// <summary>
/// Resultado de restaurar: quantos pacotes entraram em algum dispositivo, quantos já estavam em dia (ou não têm dispositivo
/// neste PC) e quais falharam.
/// </summary>
public sealed record DriverRestoreResult(int Total, int Installed, int Current, IReadOnlyList<string> Failed, bool Reboot, bool Canceled);

/// <summary>O que aconteceu com um pacote na restauração: entrou em algum dispositivo, já estava em dia (ou sem dispositivo), ou falhou.</summary>
public enum DriverRestoreOutcome { Installed, Current, Failed }

/// <summary>
/// Backup e restauração dos drivers de terceiros (os pacotes oem*.inf do repositório de drivers) com o pnputil do Windows.
/// Exportar copia cada pacote numa subpasta; restaurar adiciona cada pacote de volta ao repositório e instala nos dispositivos
/// em que ele é o melhor driver. O Windows não força um driver de posição menor: um driver mais novo já instalado continua.
/// </summary>
public static partial class DriverBackup
{
    public const string ManifestName = "corebi-drivers.json";
    private static string PnpUtil => Path.Combine(Environment.SystemDirectory, "pnputil.exe");

    /// <summary>Pasta padrão: Documentos\CoreBi-Drivers (num PC formatado ela some; o ideal é guardar fora do disco do Windows).</summary>
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CoreBi-Drivers");

    [GeneratedRegex(@"\boem\d+\.inf\b", RegexOptions.IgnoreCase)]
    private static partial Regex OemInfRegex();

    private sealed record Manifest(int Version, DateTime Created, string Computer, string Windows, int Count, long Bytes);

    /// <summary>Quantos pacotes de terceiros o Windows tem (o "oemNN.inf" não muda com o idioma da saída).</summary>
    public static async Task<int> CountInstalledAsync(CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync(PnpUtil, new[] { "/enum-drivers" }, token).ConfigureAwait(false);
        return OemInfRegex().Matches(result.Output).Select(m => m.Value.ToLowerInvariant()).Distinct().Count();
    }

    /// <summary>
    /// Exporta todos os pacotes para uma pasta nova dentro de <paramref name="root"/>. O progresso conta as subpastas que o
    /// pnputil já criou contra o total de pacotes, a cada 400 ms.
    /// </summary>
    public static async Task<DriverBackupInfo> ExportAsync(string root, Action<int, int> progress, CancellationToken token)
    {
        int total = 0;
        try { total = await CountInstalledAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { DiagnosticLog.Write("drivers.backup.count", ex); }

        DateTime now = DateTime.Now;
        string name = $"Drivers {Environment.MachineName} {now:yyyy-MM-dd HH'h'mm}";
        string folder = Path.Combine(root, name);
        for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(root, $"{name} ({i})");
        Directory.CreateDirectory(folder);
        progress(0, total);

        var run = ProcessRunner.RunAsync(PnpUtil, new[] { "/export-driver", "*", folder }, token);
        while (!run.IsCompleted)
        {
            await Task.WhenAny(run, Task.Delay(400, CancellationToken.None)).ConfigureAwait(false);
            progress(SafeCount(folder), total);
        }
        ProcessResult result;
        try { result = await run.ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Cancelado no meio: a cópia pela metade não serve de backup e sairia como "o mais novo" na restauração
            try { Directory.Delete(folder, recursive: true); } catch (Exception ex) { DiagnosticLog.Write("drivers.backup.cancel", ex); }
            throw;
        }
        int count = SafeCount(folder);
        if (count == 0)
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
            throw new IOException(result.ExitCode == 0 ? "Nenhum driver de terceiros para copiar." : $"O pnputil não exportou nada (código 0x{result.ExitCode:X8}).");
        }
        long bytes = FolderBytes(folder);
        var manifest = new Manifest(1, now, Environment.MachineName, Environment.OSVersion.VersionString, count, bytes);
        AtomicJsonFile.Write(Path.Combine(folder, ManifestName), manifest);
        progress(count, Math.Max(total, count));
        return new DriverBackupInfo(folder, now, Environment.MachineName, count, bytes);
    }

    /// <summary>
    /// Lê um backup: a pasta de um backup (com o arquivo do CoreBi ou só as subpastas com .inf, como as de versões antigas) ou a
    /// pasta que guarda vários, e aí vale o mais novo. Sem nenhum pacote, null.
    /// </summary>
    public static DriverBackupInfo? Find(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            if (Describe(folder) is { } own) return own;
            return Directory.EnumerateDirectories(folder).Select(Describe).OfType<DriverBackupInfo>()
                .OrderByDescending(b => b.Created).FirstOrDefault();
        }
        catch (Exception ex) { DiagnosticLog.Write("drivers.backup.find", ex); return null; }
    }

    private static DriverBackupInfo? Describe(string folder)
    {
        string manifestPath = Path.Combine(folder, ManifestName);
        if (File.Exists(manifestPath))
        {
            try
            {
                if (new FileInfo(manifestPath).Length < 65_536 && JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath)) is { } m)
                    return new DriverBackupInfo(folder, m.Created, m.Computer, Packages(folder).Count, m.Bytes);
            }
            catch (Exception ex) { DiagnosticLog.Write("drivers.backup.manifest", ex); }
        }
        var packages = Packages(folder);
        if (packages.Count == 0) return null;
        return new DriverBackupInfo(folder, Directory.GetCreationTime(folder), "", packages.Count, FolderBytes(folder));
    }

    /// <summary>Os .inf do backup: um por subpasta de pacote, no primeiro nível (o pnputil exporta assim).</summary>
    internal static List<string> Packages(string folder)
    {
        var list = new List<string>();
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(folder))
                list.AddRange(Directory.EnumerateFiles(dir, "*.inf").OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) { DiagnosticLog.Write("drivers.backup.packages", ex); }
        return list;
    }

    /// <summary>
    /// Devolve os drivers do backup ao Windows, um pacote por vez (assim o código de cada um diz o que aconteceu):
    /// 0 = instalado em algum dispositivo, 259 = já em dia ou sem dispositivo neste PC, 3010 = instalado e falta reiniciar.
    /// <paramref name="stop"/> só é olhado entre um pacote e outro: o pnputil que está instalando nunca é interrompido no meio.
    /// <paramref name="outcome"/> diz o que aconteceu com cada pacote (pela posição), para a janela pintar a faixa.
    /// </summary>
    public static async Task<DriverRestoreResult> RestoreAsync(DriverBackupInfo backup, Action<int, int, string> progress, CancellationToken stop,
        Action<int, DriverRestoreOutcome>? outcome = null)
    {
        var packages = Packages(backup.Folder);
        int installed = 0, current = 0, done = 0;
        bool reboot = false;
        var failed = new List<string>();
        foreach (string inf in packages)
        {
            if (stop.IsCancellationRequested) return new DriverRestoreResult(packages.Count, installed, current, failed, reboot, true);
            string name = PackageName(inf);
            progress(done, packages.Count, name);
            DriverRestoreOutcome what;
            try
            {
                var result = await ProcessRunner.RunAsync(PnpUtil, new[] { "/add-driver", inf, "/install" }, CancellationToken.None).ConfigureAwait(false);
                switch (result.ExitCode)
                {
                    case 0: installed++; what = DriverRestoreOutcome.Installed; break;
                    case 259: current++; what = DriverRestoreOutcome.Current; break;
                    case 3010: installed++; reboot = true; what = DriverRestoreOutcome.Installed; break;
                    default: failed.Add($"{name} (código {result.ExitCode})"); what = DriverRestoreOutcome.Failed; break;
                }
            }
            catch (Exception ex) { failed.Add($"{name} ({ex.Message})"); what = DriverRestoreOutcome.Failed; }
            outcome?.Invoke(done, what);
            progress(++done, packages.Count, name);
        }
        return new DriverRestoreResult(packages.Count, installed, current, failed, reboot, false);
    }

    /// <summary>Ponto de restauração antes de restaurar (até 60 s; o Windows cria no máximo um por dia).</summary>
    public static Task<string> RestorePointAsync(CancellationToken token) =>
        DriverInstaller.TryRestorePointAsync(token, "CoreBi - antes de restaurar drivers");

    /// <summary>"nvlti.inf_amd64_6f3b…" → "nvlti.inf": o nome do pacote sem o sufixo que o repositório de drivers acrescenta.</summary>
    private static string PackageName(string inf) => Path.GetFileName(inf);

    private static int SafeCount(string folder)
    {
        try { return Directory.GetDirectories(folder).Length; } catch { return 0; }
    }

    private static long FolderBytes(string folder)
    {
        try { return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }
}
