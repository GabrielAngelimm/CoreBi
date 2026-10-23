using CoreBi.Engine.Persistence;
namespace CoreBi.Engine.Infrastructure;

/// <summary>Local, bounded diagnostics. Never uploads logs or records API request headers.</summary>
public static class DiagnosticLog
{
    private static readonly object Sync = new();
    public static string Folder => Path.Combine(AppSettings.Folder, "Logs");

    public static void Write(string operation, Exception error)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, "corebi.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 1_048_576)
                    File.Move(path, path + ".previous", overwrite: true);
                // Messages from network/process responses can contain credentials. Keep type and call site only.
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {operation}: {error.GetType().FullName}\n{error.StackTrace}\n");
            }
        }
        catch (Exception) { /* Logging must not replace the original failure. */ }
    }
}
