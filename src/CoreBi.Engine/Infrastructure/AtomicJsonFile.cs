using System.Text.Json;

namespace CoreBi.Engine.Infrastructure;

internal static class AtomicJsonFile
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write<T>(string path, T value)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(file, value, Options);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
