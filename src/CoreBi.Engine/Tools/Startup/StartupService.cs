using System.Text.Json;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Startup;

/// <summary>Persists recovery data before changing Windows; removes it only after a complete restore.</summary>
public sealed class StartupService
{
    private readonly IStartupMutations _windows;
    private readonly StartupBackupStore _backups;
    public StartupService() : this(new WindowsStartupMutations(), new StartupBackupStore()) { }
    internal StartupService(IStartupMutations windows, StartupBackupStore backups) { _windows = windows; _backups = backups; }
    public IReadOnlyList<StartupBackup> Backups => _backups.List();

    public void Remove(StartupEntry entry)
    {
        var backup = _windows.Prepare(entry, _backups.Folder);
        _backups.Save(backup); // Failure here leaves the Windows entry untouched.
        _windows.Remove(entry, backup);
    }

    public void Restore(StartupBackup selected)
    {
        // Ignore commands/paths from editable UI/settings: use the protected canonical record.
        var backup = _backups.Read(selected.Id);
        _windows.Restore(backup);
        _backups.Delete(backup.Id);
    }
}

internal interface IStartupMutations
{
    StartupBackup Prepare(StartupEntry entry, string backupFolder);
    void Remove(StartupEntry entry, StartupBackup snapshot);
    void Restore(StartupBackup backup);
}

internal sealed class WindowsStartupMutations : IStartupMutations
{
    public StartupBackup Prepare(StartupEntry entry, string folder) => StartupManager.PrepareRemoval(entry, folder);
    public void Remove(StartupEntry entry, StartupBackup snapshot) => StartupManager.RemovePrepared(entry, snapshot);
    public void Restore(StartupBackup backup) => StartupManager.Restore(backup);
}

internal sealed class StartupBackupStore
{
    private readonly string? _testFolder;
    public StartupBackupStore() { }
    internal StartupBackupStore(string folder) => _testFolder = folder;
    public string Folder => _testFolder ?? ProtectedWorkspace.GetFolder("Startup");
    private string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("Identificador de backup inválido ou legado.");
        return Path.Combine(Folder, id + ".json");
    }
    public void Save(StartupBackup backup) => AtomicJsonFile.Write(PathFor(backup.Id), backup);
    public StartupBackup Read(string id)
    {
        var backup = JsonSerializer.Deserialize<StartupBackup>(File.ReadAllText(PathFor(id))) ?? throw new IOException("Backup inválido.");
        if (backup.Id != id) throw new IOException("Identidade do backup inconsistente.");
        return backup;
    }
    public void Delete(string id) => File.Delete(PathFor(id));
    public IReadOnlyList<StartupBackup> List()
    {
        Directory.CreateDirectory(Folder);
        var result = new List<StartupBackup>();
        foreach (string file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            var backup = Read(Path.GetFileNameWithoutExtension(file));
            result.Add(backup);
        }
        return result;
    }
}
