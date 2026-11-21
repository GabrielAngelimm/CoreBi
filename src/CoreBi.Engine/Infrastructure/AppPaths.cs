namespace CoreBi.Engine.Infrastructure;

public static class AppPaths
{
    private static readonly object Sync = new();
    private static string? _overrideFolder;

    public static string DataFolder
    {
        get
        {
            lock (Sync)
            {
                if (_overrideFolder is { Length: > 0 } custom) return custom;
            }
            return Environment.GetEnvironmentVariable("COREBI_DATA_DIR") is { Length: > 0 } env
                ? Path.GetFullPath(env)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoreBi");
        }
    }

    internal static IDisposable OverrideDataFolder(string folder)
    {
        string full = Path.GetFullPath(folder);
        string? previous;
        lock (Sync)
        {
            previous = _overrideFolder;
            _overrideFolder = full;
        }
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (Sync) _overrideFolder = previous;
        }
    }
}
