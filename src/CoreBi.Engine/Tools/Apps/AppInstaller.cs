using System.ComponentModel;


using System.Runtime.CompilerServices;

using Microsoft.Win32;
using CoreBi.Engine.Infrastructure;
using System.Text.RegularExpressions;

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
/// Catálogo de apps essenciais e instalação silenciosa pelo winget (Gerenciador de Pacotes do Windows), que baixa
/// sempre a versão oficial mais recente do site do fabricante e confere o hash do instalador.
/// </summary>
public static class AppInstaller
{
    public static List<InstallableApp> Catalog() => new()
    {
        new() { Name = "Steam", Description = "Loja e biblioteca de jogos da Valve.", WingetId = "Valve.Steam", DisplayPrefix = "Steam" },
        new() { Name = "Google Chrome", Description = "Navegador do Google.", WingetId = "Google.Chrome", DisplayPrefix = "Google Chrome" },
        new() { Name = "Discord", Description = "Chat de voz e texto para jogar com os amigos.", WingetId = "Discord.Discord", DisplayPrefix = "Discord" },
        new() { Name = "Lightshot", Description = "Captura de tela rápida com a tecla Print Screen.", WingetId = "Skillbrains.Lightshot", DisplayPrefix = "Lightshot" },
        new() { Name = "7-Zip", Description = "Compactador gratuito (zip, rar, 7z). Abre e cria arquivos compactados.", WingetId = "7zip.7zip", DisplayPrefix = "7-Zip" },
        new() { Name = "WinRAR", Description = "Compactador clássico (pago após o teste). Escolha ele ou o 7-Zip.", WingetId = "RARLab.WinRAR", DisplayPrefix = "WinRAR", PreSelect = false },
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

    /// <summary>Instala um app em silêncio. Retorna (ok, mensagem curta).</summary>
    public static async Task<(bool Ok, string Message)> InstallAsync(InstallableApp app, CancellationToken ct, bool upgrade = false)
    {
        var result = await ProcessRunner.RunAsync(WingetPath, BuildArguments(app.WingetId, upgrade), ct).ConfigureAwait(false);
        string output = result.Output + result.Error;

        return result.ExitCode switch
        {
            0 => (true, "Instalado."),
            unchecked((int)0x8A15002B) or unchecked((int)0x8A150061) => (true, "Já estava instalado."),
            unchecked((int)0x8A150014) => (false, "Pacote não encontrado no winget."),
            unchecked((int)0x8A150008) => (false, "Falha no download: confira a internet e tente de novo."),
            _ => (false, $"Falhou (código 0x{result.ExitCode:X8}). {LastLine(output)}".Trim()),
        };
    }

    internal static string[] BuildArguments(string package, bool upgrade)
    {
        if (!Regex.IsMatch(package, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Identificador de pacote inválido.", nameof(package));
        return new[] { upgrade ? "upgrade" : "install", "--id", package, "--exact", "--source", "winget", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" };
    }

    /// <summary>Última linha útil da saída do winget (sem as barras de progresso).</summary>
    private static string LastLine(string output) =>
        output.Split('\n', '\r').Select(l => l.Trim())
              .LastOrDefault(l => l.Length > 3 && !l.All(c => "-\\|/█▒ ".Contains(c)) && !l.Contains("KB /") && !l.Contains("MB /")) ?? "";
}
