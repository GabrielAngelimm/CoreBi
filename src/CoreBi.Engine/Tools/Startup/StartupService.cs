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

    public List<StartupEntry> List() => StartupManager.List();

    public void SetEnabled(StartupEntry entry, bool enabled) => StartupManager.SetEnabled(entry, enabled);

    public void Remove(StartupEntry entry)
    {
        var backup = _windows.Prepare(entry, _backups.Folder);
        try
        {
            _backups.Save(backup); // Failure here leaves the Windows entry untouched.
        }
        catch
        {
            TryDeleteFile(backup.Backup);
            throw;
        }

        try
        {
            _windows.Remove(entry, backup);
        }
        catch
        {
            // Se a remoção no Windows falhou, desfaz o backup recém-salvo para não deixar entrada fantasma.
            try { _backups.Delete(backup.Id); } catch (Exception ex) { DiagnosticLog.Write("startup.rollback", ex); }
            TryDeleteFile(backup.Backup);
            throw;
        }
    }

    public void Restore(StartupBackup selected)
    {
        // Ignore commands/paths from editable UI/settings: use the protected canonical record.
        var backup = _backups.Read(selected.Id);
        _windows.Restore(backup);
        _backups.Delete(backup.Id);
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { DiagnosticLog.Write("startup.rollback", ex); }
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
