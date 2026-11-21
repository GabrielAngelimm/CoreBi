using System.Runtime.InteropServices;

namespace CoreBi.Engine.Performance.Benchmarks;

/// <summary>Buffer nativo alinhado em página (4096 B) exigido por FILE_FLAG_NO_BUFFERING no Windows.</summary>
internal sealed unsafe class AlignedBuffer : IDisposable
{
    public const int PageAlignment = 4096;
    private void* _ptr;

    public int Length { get; }

    public AlignedBuffer(int length, int alignment = PageAlignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        _ptr = NativeMemory.AlignedAlloc((nuint)length, (nuint)alignment);
        if (_ptr == null) throw new OutOfMemoryException();
        Length = length;
    }

    public Span<byte> AsSpan(int length)
    {
        ObjectDisposedException.ThrowIf(_ptr == null, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Length);
        return new Span<byte>(_ptr, length);
    }

    public Span<byte> AsSpan() => AsSpan(Length);

    public void Dispose()
    {
        if (_ptr == null) return;
        NativeMemory.AlignedFree(_ptr);
        _ptr = null;
    }
}
