using System.Diagnostics;

using Microsoft.Win32;
using System.Security.Cryptography;


namespace CoreBi.Engine.Tools.Startup;

/// <summary>Programa que abre junto com o Windows (chave Run do registro ou atalho na pasta Inicializar).</summary>
public sealed class StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";          // ex.: "Registro · usuário"
    public bool Machine { get; init; }                 // HKLM / pasta comum: precisa de administrador
    public bool Enabled { get; set; }
    public string Detail { get; init; } = "";          // descrição · fabricante
    public bool FileMissing { get; init; }

    internal string Kind { get; init; } = "";          // Run | Run32 | Folder
    internal string? FilePath { get; init; }           // só para Folder

    public string State => FileMissing ? "ARQUIVO NÃO EXISTE" : Enabled ? "ATIVADO" : "DESATIVADO";
}

/// <summary>Item removido, guardado para "Restaurar removidos".</summary>
public sealed class StartupBackup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ValueKind { get; set; } = (int)RegistryValueKind.String;
    public string? FileHash { get; set; }
    public byte[]? ApprovedState { get; set; }
    public bool ApprovedStateCaptured { get; set; }
    public string Kind { get; set; } = "";
    public bool Machine { get; set; }
    public string Name { get; set; } = "";
    public string? Command { get; set; }   // registro
    public string? Backup { get; set; }    // pasta: cópia do atalho em %AppData%\CoreBi\Startup
    public string? Original { get; set; }  // pasta: caminho original
}

/// <summary>
/// Lista, ativa/desativa e remove programas de inicialização. Desativar usa as mesmas chaves StartupApproved do
/// Gerenciador de Tarefas (a entrada continua lá e pode ser religada); remover apaga a entrada, com cópia para desfazer.
/// </summary>
public static class StartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        ReadRun(list, RegistryHive.CurrentUser, RegistryView.Default, "Run", false, "Registro · usuário");
        ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", true, "Registro · todos os usuários");
        ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", true, "Registro · todos os usuários (32 bits)");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), false, "Pasta Inicializar · usuário");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), true, "Pasta Inicializar · todos os usuários");
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
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
                string cmd = key.GetValue(name)?.ToString() ?? "";
                string? exe = ExeFromCommand(cmd);
                list.Add(new StartupEntry
                {
                    Name = name,
                    Command = cmd,
                    Source = source,
                    Machine = machine,
                    Kind = kind,
                    Enabled = IsApproved(machine, kind, name),
                    Detail = Describe(exe),
                    FileMissing = exe != null && !File.Exists(exe),
                });
            }
        }
        catch { }
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
                string? target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) : null;
                string? exe = target != null ? ExeFromCommand(target) : file;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(fn),
                    Command = target ?? file,
                    Source = source,
                    Machine = machine,
                    Kind = "Folder",
                    FilePath = file,
                    Enabled = IsApproved(machine, "StartupFolder", fn),
                    Detail = Describe(exe),
                    FileMissing = target != null && exe != null && !File.Exists(exe),
                });
            }
        }
        catch { }
    }

    // ----- ativar / desativar (StartupApproved, como o Gerenciador de Tarefas) -----

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
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        using var key = (e.Machine ? Registry.LocalMachine : Registry.CurrentUser).CreateSubKey(ApprovedPath + ApprovedKind(e));
        key.SetValue(ApprovedName(e), data, RegistryValueKind.Binary);
        e.Enabled = enabled;
    }

    // ----- remover / restaurar -----

    internal static StartupBackup PrepareRemoval(StartupEntry entry, string folder)
    {
        ValidateKind(entry.Kind);
        var backup = new StartupBackup { Kind = entry.Kind, Machine = entry.Machine, Name = entry.Name, Original = entry.FilePath };
        using (var approved = (entry.Machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(ApprovedPath + ApprovedKind(entry)))
            backup.ApprovedState = approved?.GetValue(ApprovedName(entry)) as byte[];
        backup.ApprovedStateCaptured = true;
        if (entry.Kind == "Folder")
        {
            ValidateStartupPath(entry.FilePath!, entry.Machine);
            Directory.CreateDirectory(folder);
            backup.Backup = Path.Combine(folder, backup.Id + Path.GetExtension(entry.FilePath));
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

    // ----- utilidades -----

    /// <summary>Extrai o executável da linha de comando ("C:\x\a.exe" -min  →  C:\x\a.exe).</summary>
    public static string? ExeFromCommand(string cmd)
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
        // "rundll32.exe ..." e afins: procura no System32; se não achar, não dá para saber onde está
        string sys = Path.Combine(Environment.SystemDirectory, path);
        return File.Exists(sys) ? sys : null;
    }

    private static string Describe(string? exe)
    {
        try
        {
            if (exe == null || !File.Exists(exe)) return "";
            var v = FileVersionInfo.GetVersionInfo(exe);
            return string.Join(" · ", new[] { v.FileDescription, v.CompanyName }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).Distinct());
        }
        catch { return ""; }
    }

    private static string? ShortcutTarget(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic sc = shell.CreateShortcut(lnk);
            string target = sc.TargetPath;
            string args = sc.Arguments;
            if (string.IsNullOrEmpty(target)) return null;
            return string.IsNullOrEmpty(args) ? target : $"\"{target}\" {args}";
        }
        catch { return null; }
    }
}
