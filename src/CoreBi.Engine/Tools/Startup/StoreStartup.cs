using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CoreBi.Engine.Tools.Startup;

/// <summary>
/// Tarefas de inicialização de apps da Microsoft Store (WhatsApp, Teams, Terminal…). Elas não ficam nas chaves Run: cada app
/// declara a tarefa no manifesto e o estado mora em SystemAppData, a mesma chave que o Gerenciador de Tarefas lê e grava.
/// Estados: 0 desligada (o app nunca pediu), 1 desligada por quem usa, 2 ligada, 3 desligada por política, 4 ligada por política.
/// </summary>
internal static class StoreStartup
{
    private const string AppModel = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\";
    private const string SystemAppData = AppModel + "SystemAppData";
    private const string Packages = AppModel + @"Repository\Packages";

    public static void Read(List<StartupEntry> list)
    {
        try
        {
            using var data = Registry.CurrentUser.OpenSubKey(SystemAppData);
            using var repository = Registry.CurrentUser.OpenSubKey(Packages);
            if (data == null || repository == null) return;
            string[] installed = repository.GetSubKeyNames();
            foreach (string family in data.GetSubKeyNames())
            {
                try
                {
                    using var app = data.OpenSubKey(family);
                    if (app == null) continue;
                    foreach (string task in app.GetSubKeyNames())
                    {
                        using var key = app.OpenSubKey(task);
                        if (key?.GetValue("State") is not int state) continue;
                        // Restos de apps desinstalados ficam em SystemAppData: só entra o que ainda tem pacote instalado
                        if (Package(repository, installed, family) is not { } package) continue;
                        list.Add(new StartupEntry
                        {
                            Name = task,
                            Title = package.Title.Length > 0 ? package.Title : family.Split('_')[0],
                            Publisher = package.Publisher,
                            Command = package.Root ?? family,
                            Source = "App da Microsoft Store",
                            Machine = false,
                            Kind = "Store",
                            FilePath = $@"{SystemAppData}\{family}\{task}",
                            Enabled = state is 2 or 4,
                            Detail = "",
                            IconPath = package.Logo,
                        });
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Liga (2) ou desliga (1, "desligada por quem usa") como o Gerenciador de Tarefas; estados de política não mudam.</summary>
    public static void SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.FilePath == null || !entry.FilePath.StartsWith(SystemAppData + @"\", StringComparison.Ordinal)) throw new IOException("Entrada de app inválida.");
        using var key = Registry.CurrentUser.OpenSubKey(entry.FilePath, writable: true) ?? throw new IOException("O app não tem mais essa tarefa. Atualize a lista.");
        if (key.GetValue("State") is int state and (3 or 4)) throw new IOException("Uma política do Windows define este app; não dá para mudar por aqui.");
        key.SetValue("State", enabled ? 2 : 1, RegistryValueKind.DWord);
        if (enabled) key.SetValue("UserEnabledStartupOnce", 1, RegistryValueKind.DWord);
    }

    private sealed record PackageInfo(string Title, string Publisher, string? Root, string? Logo);

    /// <summary>Pacote instalado da família "Nome_IdDoPublicador": a versão mais nova entre "Nome_versão_arq__IdDoPublicador".</summary>
    private static PackageInfo? Package(RegistryKey repository, string[] installed, string family)
    {
        int cut = family.LastIndexOf('_');
        if (cut <= 0) return null;
        string name = family[..cut], publisherId = family[(cut + 1)..];
        string? full = installed.Where(p => p.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
                                            && p.EndsWith("_" + publisherId, StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (full == null) return null;
        using var key = repository.OpenSubKey(full);
        string title = Resolve(key?.GetValue("DisplayName") as string, full);
        string? root = key?.GetValue("PackageRootFolder") as string;
        string publisher = "", logo = "";
        try
        {
            string manifest = root != null ? Path.Combine(root, "AppxManifest.xml") : "";
            if (manifest.Length > 0 && File.Exists(manifest))
            {
                var doc = XDocument.Load(manifest);
                XElement? props = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Properties");
                publisher = Resolve(props?.Elements().FirstOrDefault(e => e.Name.LocalName == "PublisherDisplayName")?.Value, full);
                logo = props?.Elements().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value ?? "";
                // O ícone do app (o da lista Iniciar) desenha melhor pequeno que o logo da loja
                var visual = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
                string? square = visual?.Attribute("Square44x44Logo")?.Value;
                if (!string.IsNullOrEmpty(square)) logo = square;
                if (title.Length == 0) title = Resolve(visual?.Attribute("DisplayName")?.Value, full);
            }
        }
        catch { }
        return new PackageInfo(title, publisher, root, root != null && logo.Length > 0 ? AssetFile(root, logo) : null);
    }

    /// <summary>
    /// "Assets\Square44x44Logo.png" não existe com esse nome: o pacote traz variantes ("….targetsize-32.png", "….scale-100.png").
    /// Fica a de 32 px quando há, senão a escala 100 ou a primeira.
    /// </summary>
    internal static string? AssetFile(string root, string relative)
    {
        try
        {
            string path = Path.Combine(root, relative);
            if (File.Exists(path)) return path;
            string? dir = Path.GetDirectoryName(path);
            if (dir == null || !Directory.Exists(dir)) return null;
            string stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            var files = Directory.EnumerateFiles(dir, stem + ".*" + ext).ToList();
            string[] prefer = { ".targetsize-32" + ext, ".targetsize-32_altform-unplated" + ext, ".targetsize-48" + ext, ".scale-100" + ext, ".scale-200" + ext };
            foreach (string p in prefer)
                if (files.FirstOrDefault(f => f.EndsWith(p, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
            return files.FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Nomes como "ms-resource:AppName" ou "@{Pacote?ms-resource://…}" saem do arquivo de recursos do pacote.</summary>
    private static string Resolve(string? text, string packageFullName)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (!text.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("@{", StringComparison.Ordinal)) return text.Trim();
        string source = text.StartsWith("@{", StringComparison.Ordinal) ? text
            : text.StartsWith("ms-resource://", StringComparison.OrdinalIgnoreCase) ? $"@{{{packageFullName}?{text}}}"
            : text.Contains('/') ? $"@{{{packageFullName}?ms-resource://{packageFullName.Split('_')[0]}/{text[12..]}}}"
            : $"@{{{packageFullName}?ms-resource://{packageFullName.Split('_')[0]}/resources/{text[12..]}}}";
        var buffer = new StringBuilder(512);
        return SHLoadIndirectString(source, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString().Trim() : "";
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);
}
