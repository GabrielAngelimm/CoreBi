using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;
using Microsoft.Win32;

namespace CoreBi.Engine.Tools.Apps;

/// <summary>
/// Instalação via winget (Gerenciador de Pacotes do Windows). O winget baixa sempre a versão oficial mais recente do endereço
/// do fabricante que está no manifesto e confere o SHA-256 do instalador antes de abrir.
/// </summary>
public static partial class AppInstaller
{
    public const string PawnIOPackageId = "namazso.PawnIO";

    // Códigos de retorno documentados do winget (AppInstallerErrors.h)
    private const int WingetNoApplicableUpgrade = unchecked((int)0x8A15002B);
    private const int WingetAlreadyInstalled = unchecked((int)0x8A150061);
    private const int WingetNotFound = unchecked((int)0x8A150014);
    private const int WingetDownloadFailed = unchecked((int)0x8A150008);

    // IDs do winget: letras, números, ponto, hífen, sublinhado e "+" (Notepad++.Notepad++, Microsoft.VCRedist.2015+.x64)
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();

    /// <summary>O catálogo da página (ver <see cref="AppCatalog"/>).</summary>
    public static List<InstallableApp> Catalog() => AppCatalog.Create();

    internal static string WingetPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");

    public static bool WingetAvailable => File.Exists(WingetPath);

    /// <summary>Nomes em "Aplicativos instalados" (máquina 64/32 bits e usuário atual).</summary>
    public static HashSet<string> InstalledNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32),
                                             (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var hiveRoot = RegistryKey.OpenBaseKey(hive, view);
                using var root = hiveRoot.OpenSubKey(path);
                if (root == null) continue;
                foreach (var sub in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is string n && !string.IsNullOrWhiteSpace(n)) names.Add(n.Trim());
                }
            }
            catch { }
        }
        return names;
    }

    /// <summary>
    /// IDs dos pacotes que o winget reconhece como instalados (pela fonte winget), lidos de um "winget export". Inclui os MSIX, que
    /// não aparecem no registro. Nulo se o winget não respondeu (sem winget, fonte desatualizada, cancelado).
    /// </summary>
    public static async Task<HashSet<string>?> InstalledPackageIdsAsync(CancellationToken ct)
    {
        if (!WingetAvailable) return null;
        string file = Path.Combine(Path.GetTempPath(), $"corebi-winget-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
        try
        {
            var args = new[] { "export", "--output", file, "--source", "winget", "--accept-source-agreements", "--disable-interactivity" };
            // O código de saída não importa: o export sai com erro quando algum app não casa com a fonte, mas grava o arquivo
            await ProcessRunner.RunAsync(WingetPath, args, ct).ConfigureAwait(false);
            if (!File.Exists(file)) return null;
            return ParseExport(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { DiagnosticLog.Write("apps.export", ex); return null; }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    /// <summary>IDs do JSON do "winget export" (Sources[].Packages[].PackageIdentifier).</summary>
    internal static HashSet<string> ParseExport(string json)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Sources", out var sources) || sources.ValueKind != JsonValueKind.Array) return ids;
        foreach (var source in sources.EnumerateArray())
        {
            if (!source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array) continue;
            foreach (var package in packages.EnumerateArray())
                if (package.TryGetProperty("PackageIdentifier", out var id) && id.GetString() is { Length: > 0 } s) ids.Add(s);
        }
        return ids;
    }

    /// <summary>Um pacote está instalado se o winget o lista, se o nome dele aparece no registro ou se o teste próprio acha.</summary>
    public static bool IsInstalled(AppPackage package, IEnumerable<string> installedNames, ISet<string>? wingetIds = null)
    {
        if (wingetIds != null && wingetIds.Contains(package.Id)) return true;
        if (package.Probe != null && package.Probe()) return true;
        return package.DisplayPattern is { } pattern && installedNames.Any(n => pattern.IsMatch(n));
    }

    /// <summary>O cartão conta como instalado só com todos os pacotes dele presentes (Visual C++ antigos são dez).</summary>
    public static bool IsInstalled(InstallableApp app, IEnumerable<string> installedNames, ISet<string>? wingetIds = null)
    {
        var names = installedNames as ICollection<string> ?? installedNames.ToList();
        return app.Packages.Count > 0 && app.Packages.All(p => IsInstalled(p, names, wingetIds));
    }

    public static void RefreshInstalled(IEnumerable<InstallableApp> apps, ISet<string>? wingetIds = null)
    {
        var names = InstalledNames();
        foreach (var a in apps)
        {
            a.Installed = IsInstalled(a, names, wingetIds);
            if (a.Installed) a.Selected = false;
        }
    }

    /// <summary>Instala em silêncio os pacotes de um cartão, um por vez. Retorna (ok, mensagem curta); falha no primeiro que falhar.</summary>
    public static async Task<(bool Ok, string Message)> InstallAsync(InstallableApp app, CancellationToken ct)
    {
        bool any = false;
        foreach (var package in app.Packages)
        {
            var (ok, installed, message) = await InstallPackageAsync(package.Id, ct).ConfigureAwait(false);
            if (!ok) return (false, app.Packages.Count > 1 ? $"{package.Id}: {message}" : message);
            any |= installed;
        }
        return (true, any ? "Instalado." : "Já estava instalado.");
    }

    private static async Task<(bool Ok, bool Installed, string Message)> InstallPackageAsync(string id, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(WingetPath, BuildArguments(id), ct).ConfigureAwait(false);
        string output = result.Output + result.Error;

        return result.ExitCode switch
        {
            0 => (true, true, "Instalado."),
            WingetNoApplicableUpgrade or WingetAlreadyInstalled => (true, false, "Já estava instalado."),
            WingetNotFound => (false, false, "Pacote não encontrado no winget."),
            WingetDownloadFailed => (false, false, "Falha no download: confira a internet e tente de novo."),
            _ => (false, false, $"Falhou (código 0x{result.ExitCode:X8}). {LastLine(output)}".Trim()),
        };
    }

    /// <summary>Abre o winget com janela visível para pacotes que exigem confirmação interativa (ex.: driver PawnIO).</summary>
    public static async Task<int> InstallInteractiveAsync(string packageId, CancellationToken ct)
    {
        var start = new ProcessStartInfo(WingetPath) { UseShellExecute = false };
        foreach (string arg in BuildArguments(packageId, silent: false))
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o winget.");
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode;
    }

    internal static string[] BuildArguments(string package, bool silent = true)
    {
        if (!PackageIdRegex().IsMatch(package))
            throw new ArgumentException("Identificador de pacote inválido.", nameof(package));
        var args = new List<string> { "install", "--id", package, "--exact", "--source", "winget" };
        if (silent) args.Add("--silent");
        args.Add("--accept-package-agreements");
        args.Add("--accept-source-agreements");
        if (silent) args.Add("--disable-interactivity");
        return args.ToArray();
    }

    /// <summary>Última linha útil da saída do winget (sem as barras de progresso).</summary>
    private static string LastLine(string output) =>
        output.Split('\n', '\r').Select(l => l.Trim())
              .LastOrDefault(l => l.Length > 3 && !l.All(c => "-\\|/█▒ ".Contains(c)) && !l.Contains("KB /") && !l.Contains("MB /")) ?? "";
}
