using System.Buffers;
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

    /// <summary>
    /// O mesmo bloco como <see cref="Memory{T}"/>, para E/S assíncrona (overlapped). A memória nativa não se move,
    /// então não há o que fixar; quem chama só não pode usá-la depois do <see cref="Dispose"/>.
    /// </summary>
    public Memory<byte> AsMemory()
    {
        ObjectDisposedException.ThrowIf(_ptr == null, this);
        return new NativeManager((byte*)_ptr, Length).Memory;
    }

    private sealed class NativeManager : MemoryManager<byte>
    {
        private readonly byte* _start;
        private readonly int _length;

        public NativeManager(byte* start, int length) { _start = start; _length = length; }
        public override Span<byte> GetSpan() => new(_start, _length);
        public override MemoryHandle Pin(int elementIndex = 0) => new(_start + elementIndex);
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    public void Dispose()
    {
        if (_ptr == null) return;
        NativeMemory.AlignedFree(_ptr);
        _ptr = null;
    }
}
