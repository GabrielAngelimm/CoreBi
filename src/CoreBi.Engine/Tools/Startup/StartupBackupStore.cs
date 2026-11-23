using System.Text.Json;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Startup;

internal sealed class StartupBackupStore
{
    private readonly string? _testFolder;
    public StartupBackupStore() { }
    internal StartupBackupStore(string folder) => _testFolder = folder;
    public string Folder => _testFolder ?? ProtectedWorkspace.GetFolder(ProtectedArea.Startup);

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
            try
            {
                var backup = Read(Path.GetFileNameWithoutExtension(file));
                backup.RemovedAt ??= File.GetLastWriteTime(file); // backups de antes da data na ficha
                result.Add(backup);
            }
            catch (Exception ex)
            {
                // Um arquivo corrompido ou legado não deve impedir a listagem dos demais backups válidos.
                DiagnosticLog.Write("startup.backup.corrupt", ex);
            }
        }
        return result.OrderByDescending(b => b.RemovedAt).ToList();
    }
}
