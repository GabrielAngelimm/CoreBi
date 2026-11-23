using System.Diagnostics;
using System.Security.Cryptography;
using CoreBi.Engine.Infrastructure;
using Microsoft.Win32;

namespace CoreBi.Engine.Tools.Startup;

/// <summary>
/// Lista, ativa/desativa e remove programas de inicialização via chaves Run, pastas Inicializar e StartupApproved
/// (o mesmo mecanismo do Gerenciador de Tarefas).
/// </summary>
internal static class StartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        ReadRun(list, RegistryHive.CurrentUser, RegistryView.Default, "Run", false, "Registro (usuário)");
        ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", true, "Registro (todos os usuários)");
        ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", true, "Registro 32 bits (todos os usuários)");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), false, "Pasta Inicializar (usuário)");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), true, "Pasta Inicializar (todos os usuários)");
        StoreStartup.Read(list);
        return list.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadRun(List<StartupEntry> list, RegistryHive hive, RegistryView view, string kind, bool machine, string source)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(RunPath);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    string cmd = key.GetValue(name)?.ToString() ?? "";
                    string? exe = ExeFromCommand(cmd);
                    var info = Identify(name, cmd, exe);
                    list.Add(new StartupEntry
                    {
                        Name = name,
                        Title = info.Title,
                        Publisher = info.Publisher,
                        Command = cmd,
                        Source = source,
                        Machine = machine,
                        Kind = kind,
                        Enabled = IsApproved(machine, kind, name),
                        Detail = info.Description,
                        IconPath = info.Icon,
                        Location = info.Location,
                        FileMissing = exe != null && !File.Exists(exe),
                    });
                }
                catch (Exception ex) { DiagnosticLog.Write("startup.read", ex); } // uma entrada ruim não esconde as outras
            }
        }
        catch (Exception ex) { DiagnosticLog.Write("startup.read", ex); }
    }

    private static void ReadFolder(List<StartupEntry> list, string folder, bool machine, string source)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder))
            {
                string fn = Path.GetFileName(file);
                if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    string? target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) : null;
                    string? exe = target != null ? ExeFromCommand(target) : file;
                    string name = Path.GetFileNameWithoutExtension(fn);
                    var info = Identify(ShortcutName(name), target ?? file, exe);
                    list.Add(new StartupEntry
                    {
                        Name = name,
                        Title = info.Title,
                        Publisher = info.Publisher,
                        Command = target ?? file,
                        Source = source,
                        Machine = machine,
                        Kind = "Folder",
                        FilePath = file,
                        Enabled = IsApproved(machine, "StartupFolder", fn),
                        Detail = info.Description,
                        IconPath = info.Icon ?? file,
                        Location = info.Location,
                        FileMissing = target != null && exe != null && !File.Exists(exe),
                    });
                }
                catch (Exception ex) { DiagnosticLog.Write("startup.read", ex); }
            }
        }
        catch (Exception ex) { DiagnosticLog.Write("startup.read", ex); }
    }

    private static string ApprovedKind(StartupEntry e) => e.Kind == "Folder" ? "StartupFolder" : e.Kind;
    private static string ApprovedName(StartupEntry e) => e.Kind == "Folder" ? Path.GetFileName(e.FilePath!) : e.Name;

    private static bool IsApproved(bool machine, string kind, string name)
    {
        try
        {
            using var key = (machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(ApprovedPath + kind);
            // 02/06 = ativado, 03/07 = desativado; sem valor = ativado
            return key?.GetValue(name) is not byte[] { Length: > 0 } b || (b[0] & 1) == 0;
        }
        catch { return true; }
    }

    public static void SetEnabled(StartupEntry e, bool enabled)
    {
        if (e.Store) { StoreStartup.SetEnabled(e, enabled); e.Enabled = enabled; return; }
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        using var key = (e.Machine ? Registry.LocalMachine : Registry.CurrentUser).CreateSubKey(ApprovedPath + ApprovedKind(e));
        key.SetValue(ApprovedName(e), data, RegistryValueKind.Binary);
        e.Enabled = enabled;
    }

    internal static StartupBackup PrepareRemoval(StartupEntry entry, string folder)
    {
        ValidateKind(entry.Kind);
        var backup = new StartupBackup
        {
            Kind = entry.Kind,
            Machine = entry.Machine,
            Name = entry.Name,
            Original = entry.FilePath,
            RemovedAt = DateTime.Now,
            Title = entry.Title,
            Publisher = entry.Publisher,
            IconPath = entry.IconPath,
        };
        using (var approved = (entry.Machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(ApprovedPath + ApprovedKind(entry)))
            backup.ApprovedState = approved?.GetValue(ApprovedName(entry)) as byte[];
        backup.ApprovedStateCaptured = true;
        if (entry.Kind == "Folder")
        {
            ValidateStartupPath(entry.FilePath!, entry.Machine);
            Directory.CreateDirectory(folder);
            // ".bak" no fim: um atalho .json na pasta Inicializar não pode colidir com o registro <Id>.json do backup.
            backup.Backup = Path.Combine(folder, backup.Id + Path.GetExtension(entry.FilePath) + ".bak");
            File.Copy(entry.FilePath!, backup.Backup, overwrite: false);
            backup.FileHash = Hash(backup.Backup);
        }
        else
        {
            using var root = OpenRoot(entry.Machine, entry.Kind);
            using var key = root.OpenSubKey(RunPath) ?? throw new IOException("Chave Run não encontrada.");
            if (key.GetValue(entry.Name)?.ToString() != entry.Command) throw new IOException("A entrada mudou. Atualize a lista.");
            var kind = key.GetValueKind(entry.Name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) throw new IOException("Tipo de entrada não suportado.");
            backup.Command = key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
            backup.ValueKind = (int)kind;
        }
        return backup;
    }

    internal static void RemovePrepared(StartupEntry entry, StartupBackup backup)
    {
        if (entry.Kind == "Folder")
        {
            if (Hash(entry.FilePath!) != backup.FileHash) throw new IOException("O arquivo mudou. Nada foi removido.");
            File.Delete(entry.FilePath!);
        }
        else
        {
            using var root = OpenRoot(entry.Machine, entry.Kind);
            using var key = root.OpenSubKey(RunPath, writable: true) ?? throw new IOException("Chave Run não encontrada.");
            if (key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() != backup.Command
                || (int)key.GetValueKind(entry.Name) != backup.ValueKind) throw new IOException("A entrada mudou. Nada foi removido.");
            key.DeleteValue(entry.Name);
        }
    }

    internal static void Restore(StartupBackup backup)
    {
        ValidateKind(backup.Kind);
        if (backup.Kind == "Folder")
        {
            if (backup.Backup == null || backup.Original == null || !File.Exists(backup.Backup)) throw new FileNotFoundException("Arquivo de backup não encontrado.");
            ValidateStartupPath(backup.Original, backup.Machine);
            if (Hash(backup.Backup) != backup.FileHash) throw new IOException("Arquivo de backup alterado.");
            File.Move(backup.Backup, backup.Original, overwrite: false);
            try { RestoreApproval(backup); }
            catch { File.Move(backup.Original, backup.Backup, overwrite: false); throw; }
            return;
        }
        using var root = OpenRoot(backup.Machine, backup.Kind);
        using var key = root.CreateSubKey(RunPath);
        if (key.GetValueNames().Contains(backup.Name, StringComparer.OrdinalIgnoreCase)) throw new IOException("Já existe uma entrada com esse nome; nada foi sobrescrito.");
        if ((RegistryValueKind)backup.ValueKind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) throw new IOException("Tipo de backup inválido.");
        key.SetValue(backup.Name, backup.Command ?? "", (RegistryValueKind)backup.ValueKind);
        try { RestoreApproval(backup); }
        catch { key.DeleteValue(backup.Name); throw; }
    }

    private static RegistryKey OpenRoot(bool machine, string kind) => RegistryKey.OpenBaseKey(
        machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
        kind == "Run32" ? RegistryView.Registry32 : machine ? RegistryView.Registry64 : RegistryView.Default);

    private static void ValidateKind(string kind)
    {
        if (kind == "Store") throw new IOException("Apps da Microsoft Store só podem ser desligados, não removidos.");
        if (kind is not ("Run" or "Run32" or "Folder")) throw new IOException("Origem de inicialização inválida.");
    }

    private static void ValidateStartupPath(string path, bool machine)
    {
        string expected = Environment.GetFolderPath(machine ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Caminho fora da pasta Inicializar.");
    }

    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }

    private static void RestoreApproval(StartupBackup backup)
    {
        if (!backup.ApprovedStateCaptured) return;
        var entry = new StartupEntry { Kind = backup.Kind, Machine = backup.Machine, Name = backup.Name, FilePath = backup.Original };
        using var approved = (backup.Machine ? Registry.LocalMachine : Registry.CurrentUser).CreateSubKey(ApprovedPath + ApprovedKind(entry));
        if (backup.ApprovedState is byte[] data) approved.SetValue(ApprovedName(entry), data, RegistryValueKind.Binary);
        else approved.DeleteValue(ApprovedName(entry), throwOnMissingValue: false);
    }

    /// <summary>Extrai o executável da linha de comando ("C:\x\a.exe" -min → C:\x\a.exe).</summary>
    private static string? ExeFromCommand(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.Length == 0) return null;
        string path;
        if (cmd[0] == '"')
        {
            int end = cmd.IndexOf('"', 1);
            if (end <= 1) return null;
            path = cmd[1..end];
        }
        else
        {
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            int sp = cmd.IndexOf(' ');
            path = exe > 0 ? cmd[..(exe + 4)] : sp > 0 ? cmd[..sp] : cmd;
        }
        if (Path.IsPathRooted(path)) return path;
        // "rundll32.exe ..." e afins: procura no System32; se não achar, não dá para saber onde está.
        string sys = Path.Combine(Environment.SystemDirectory, path);
        return File.Exists(sys) ? sys : null;
    }

    private readonly record struct Identity(string Title, string Publisher, string Description, string? Icon, string? Location);

    /// <summary>
    /// Nome, fabricante e ícone de uma entrada. O nome é o da entrada no Windows, a não ser que seja um identificador
    /// ("GoogleChromeAutoLaunch_579CF1F2…"): aí vale o nome do produto. Pelo rundll32 o que abre é a DLL, então fabricante e
    /// ícone vêm dela; nos instaladores Squirrel (Discord, Teams antigo) o ícone vem do programa que o Update.exe abre.
    /// </summary>
    private static Identity Identify(string name, string command, string? exe)
    {
        string? module = exe;
        if (exe != null && Path.GetFileName(exe).Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase)) module = RundllModule(command) ?? exe;
        FileVersionInfo? v = null;
        try { if (module != null && File.Exists(module)) v = FileVersionInfo.GetVersionInfo(module); } catch { }
        string product = Clean(v?.ProductName), description = Clean(v?.FileDescription), company = Clean(v?.CompanyName);
        string title = name;
        if (LooksLikeId(name)) title = product.Length > 0 ? product : description.Length > 0 ? description : name;
        string? icon = module != null && File.Exists(module) ? module : null;
        if (exe != null && SquirrelTarget(exe, command) is { } app) icon = app;
        string? location = module != null && File.Exists(module) ? Path.GetDirectoryName(module) : null;
        return new Identity(title, company, description, icon, location);
    }

    private static string Clean(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().TrimEnd('.');

    /// <summary>"Discord - Atalho" → "Discord": o sufixo que o Explorer põe nos atalhos não é parte do nome.</summary>
    private static string ShortcutName(string name)
    {
        foreach (string suffix in new[] { " - Atalho", " - Shortcut", " – Atalho" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length];
        return name;
    }

    /// <summary>Nome de entrada que é identificador: um trecho hexadecimal longo, um GUID ou "AutoLaunch_".</summary>
    internal static bool LooksLikeId(string name)
    {
        if (name.Contains("AutoLaunch", StringComparison.OrdinalIgnoreCase)) return true;
        int run = 0;
        foreach (char c in name)
        {
            run = Uri.IsHexDigit(c) ? run + 1 : 0;
            if (run >= 12) return true;
        }
        return Guid.TryParse(name.Trim('{', '}'), out _);
    }

    /// <summary>"rundll32.exe C:\x\LogiLDA.dll,LogiFetch" → C:\x\LogiLDA.dll.</summary>
    private static string? RundllModule(string command)
    {
        string cmd = Environment.ExpandEnvironmentVariables(command);
        int at = cmd.IndexOf("rundll32", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        int space = cmd.IndexOf(' ', at);
        if (space < 0) return null;
        string rest = cmd[(space + 1)..].Trim();
        string path = rest.StartsWith('"') ? rest[1..].Split('"')[0] : rest.Split(',')[0].Trim();
        if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.SystemDirectory, path);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Update.exe --processStart Discord.exe: o executável do app na pasta app-* mais nova, para o ícone.</summary>
    private static string? SquirrelTarget(string exe, string command)
    {
        try
        {
            if (!Path.GetFileName(exe).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) return null;
            int at = command.IndexOf("--processStart", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            string target = command[(at + 14)..].Trim().Trim('"').Split(' ', '"')[0];
            string? root = Path.GetDirectoryName(exe);
            if (root == null || target.Length == 0) return null;
            return Directory.EnumerateDirectories(root, "app-*").OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => Path.Combine(d, target)).FirstOrDefault(File.Exists);
        }
        catch { return null; }
    }

    private static string? ShortcutTarget(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            using var scope = new ComScope();
            dynamic shell = scope.Track(Activator.CreateInstance(type)!);
            dynamic sc = scope.Track((object)shell.CreateShortcut(lnk));
            string target = sc.TargetPath;
            string args = sc.Arguments;
            if (string.IsNullOrEmpty(target)) return null;
            return string.IsNullOrEmpty(args) ? target : $"\"{target}\" {args}";
        }
        catch { return null; }
    }
}
