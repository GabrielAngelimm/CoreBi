using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;
using Microsoft.Win32;

namespace CoreBi.Engine.Tools.Apps;

/// <summary>App do catálogo "Instalar apps" (pós-formatação), instalado pelo winget.</summary>
public sealed class InstallableApp : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string WingetId { get; init; } = "";
    /// <summary>Começo do nome em "Aplicativos instalados" (Painel de Controle), para saber se já está instalado.</summary>
    public string DisplayPrefix { get; init; } = "";
    public bool PreSelect { get; init; } = true;

    private bool _selected, _installed;
    private string _status = "";

    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public bool Installed
    {
        get => _installed;
        set { Set(ref _installed, value); OnChanged(nameof(CanSelect)); }
    }
    public bool CanSelect => !Installed;
    public string Status { get => _status; set => Set(ref _status, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? p = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(p);
    }
}

/// <summary>
/// Catálogo de apps essenciais e instalação via winget (Gerenciador de Pacotes do Windows).
/// O winget baixa sempre a versão oficial mais recente do site do fabricante e confere o hash do instalador.
/// </summary>
public static partial class AppInstaller
{
    public const string PawnIOPackageId = "namazso.PawnIO";

    // Códigos de retorno documentados do winget (AppInstallerErrors.h)
    private const int WingetNoApplicableUpgrade = unchecked((int)0x8A15002B);
    private const int WingetAlreadyInstalled = unchecked((int)0x8A150061);
    private const int WingetNotFound = unchecked((int)0x8A150014);
    private const int WingetDownloadFailed = unchecked((int)0x8A150008);

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();

    public static List<InstallableApp> Catalog() => new()
    {
        new() { Name = "Steam", Description = "Loja e biblioteca de jogos da Valve.", WingetId = "Valve.Steam", DisplayPrefix = "Steam" },
        new() { Name = "Google Chrome", Description = "Navegador do Google.", WingetId = "Google.Chrome", DisplayPrefix = "Google Chrome" },
        new() { Name = "Discord", Description = "Chat de voz e texto para jogar com os amigos.", WingetId = "Discord.Discord", DisplayPrefix = "Discord" },
        new() { Name = "Lightshot", Description = "Captura de tela rápida com a tecla Print Screen.", WingetId = "Skillbrains.Lightshot", DisplayPrefix = "Lightshot" },
        new() { Name = "7-Zip", Description = "Compactador gratuito para zip, rar e 7z.", WingetId = "7zip.7zip", DisplayPrefix = "7-Zip" },
        new() { Name = "WinRAR", Description = "Compactador clássico, pago depois do teste.", WingetId = "RARLab.WinRAR", DisplayPrefix = "WinRAR", PreSelect = false },
        new() { Name = "VLC", Description = "Player de vídeo e música que abre qualquer formato.", WingetId = "VideoLAN.VLC", DisplayPrefix = "VLC media player" },
    };

    public static string WingetPath =>
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

    public static void RefreshInstalled(IEnumerable<InstallableApp> apps)
    {
        var names = InstalledNames();
        foreach (var a in apps)
        {
            a.Installed = names.Any(n => n.StartsWith(a.DisplayPrefix, StringComparison.OrdinalIgnoreCase));
            if (a.Installed) a.Selected = false;
        }
    }

    /// <summary>Instala um app em silêncio via winget. Retorna (ok, mensagem curta).</summary>
    public static async Task<(bool Ok, string Message)> InstallAsync(InstallableApp app, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(WingetPath, BuildArguments(app.WingetId), ct).ConfigureAwait(false);
        string output = result.Output + result.Error;

        return result.ExitCode switch
        {
            0 => (true, "Instalado."),
            WingetNoApplicableUpgrade or WingetAlreadyInstalled => (true, "Já estava instalado."),
            WingetNotFound => (false, "Pacote não encontrado no winget."),
            WingetDownloadFailed => (false, "Falha no download: confira a internet e tente de novo."),
            _ => (false, $"Falhou (código 0x{result.ExitCode:X8}). {LastLine(output)}".Trim()),
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
