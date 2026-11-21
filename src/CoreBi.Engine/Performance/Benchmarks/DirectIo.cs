using Microsoft.Win32.SafeHandles;

namespace CoreBi.Engine.Performance.Benchmarks;

internal static class DirectIo
{
    /// <summary>FILE_FLAG_NO_BUFFERING: lê e grava direto no dispositivo, fora do cache de páginas do Windows.</summary>
    public const FileOptions NoBuffering = (FileOptions)0x20000000;

    public static void ReadExact(SafeFileHandle handle, Span<byte> buffer, long offset, string errorMessage)
    {
        if (RandomAccess.Read(handle, buffer, offset) != buffer.Length)
            throw new EndOfStreamException(errorMessage);
    }
}
