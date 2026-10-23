using System.IO.MemoryMappedFiles;
using System.Text;

namespace CoreBi.Engine.Games;

internal enum RtssStatus { Ready, Unavailable, Incompatible, Busy, SlotsFull, ContentTooLarge }
internal sealed record RtssContent(string Text, byte[] Objects);
internal readonly record struct RtssTarget(bool Active, uint RenderedFrame);

/// <summary>
/// Cliente do protocolo público RTSSSharedMemoryV2. Não carrega DLLs do RTSS nem abre processos de jogos.
/// Cada operação abre a seção existente, permitindo reconectar após reinício do servidor sem manter handles antigos.
/// </summary>
internal sealed class RtssOverlayClient : IDisposable
{
    private const uint Signature = 0x52545353, MinimumVersion = 0x0002000E;
    private const int TextOffset = 512, TextCapacity = 4096, ObjectsOffset = 4608, ObjectsCapacity = 262144;
    private const int LargeTextOffset = ObjectsOffset + ObjectsCapacity, LargeTextCapacity = 32768;
    private readonly string _mappingName;
    private readonly byte[] _owner = Encoding.ASCII.GetBytes($"CoreBi:{Environment.ProcessId}:{Guid.NewGuid():N}\0");
    private bool _disposed;
    private bool _ownsSlot;
    internal uint PublishedFrame { get; private set; }

    internal RtssOverlayClient(string mappingName = "RTSSSharedMemoryV2") => _mappingName = mappingName;

    internal RtssStatus Inspect(int processId, out RtssTarget target)
    {
        RtssTarget result = default;
        var status = Access(memory =>
        {
            uint entrySize = Read(memory, 8), offset = Read(memory, 12), count = Read(memory, 16);
            if (!ValidArray(memory.Length, offset, count, entrySize, 336, 256)) return RtssStatus.Incompatible;
            for (int i = 0; i < count; i++)
            {
                var app = memory.Slice(checked((int)(offset + i * entrySize)), (int)entrySize);
                if (processId <= 0 || Read(app, 0) != (uint)processId) continue;
                // dwTime1 usa GetTickCount (32 bits). Subtração modular também funciona após o wrap de 49,7 dias.
                uint age = unchecked((uint)Environment.TickCount - Read(app, 272));
                result = new RtssTarget((Read(app, 264) & 0xFFFF) != 0 && age < 2500, Read(app, 332));
                break;
            }
            return RtssStatus.Ready;
        });
        target = result;
        return status;
    }

    internal RtssStatus Publish(RtssContent content)
    {
        if (content.Text.Contains('\0') || content.Objects.Length > ObjectsCapacity) return RtssStatus.ContentTooLarge;
        // RTSS utiliza texto ANSI; o formatador converte os poucos símbolos fora de Latin-1 antes desta fronteira.
        byte[] text = Encoding.Latin1.GetBytes(content.Text);
        return Access(memory =>
        {
            int size = (int)Read(memory, 20), offset = (int)Read(memory, 24), count = (int)Read(memory, 28);
            bool large = Read(memory, 4) >= 0x00020014 && size >= LargeTextOffset + LargeTextCapacity;
            int capacity = large ? LargeTextCapacity : TextCapacity;
            if (text.Length >= capacity || (content.Objects.Length > 0 && size < ObjectsOffset + ObjectsCapacity))
                return RtssStatus.ContentTooLarge;
            int slot = FindSlot(memory, offset, size, count, false);
            if (slot < 0) slot = FindSlot(memory, offset, size, count, true);
            if (slot < 0) return RtssStatus.SlotsFull;
            var entry = memory.Slice(slot, size);
            _owner.CopyTo(entry[256..]);
            _ownsSlot = true;
            // Mantém os campos legados vazios; versões novas escolhem szOSDEx2, antigas szOSDEx.
            entry[0] = entry[TextOffset] = 0;
            int textOffset = large ? LargeTextOffset : TextOffset;
            text.CopyTo(entry[textOffset..]);
            entry[textOffset + text.Length] = 0;
            content.Objects.CopyTo(entry[ObjectsOffset..]);
            PublishedFrame = IncrementFrame(memory);
            return RtssStatus.Ready;
        });
    }

    internal RtssStatus Clear()
    {
        if (!_ownsSlot) return RtssStatus.Ready;
        var status = Access(memory =>
        {
            int size = (int)Read(memory, 20), offset = (int)Read(memory, 24), count = (int)Read(memory, 28);
            int slot = FindSlot(memory, offset, size, count, false);
            if (slot >= 0)
            {
                memory.Slice(slot, size).Clear();
                IncrementFrame(memory);
            }
            return RtssStatus.Ready;
        });
        if (status == RtssStatus.Ready) _ownsSlot = false;
        return status;
    }

    private int FindSlot(Span<byte> memory, int offset, int size, int count, bool empty)
    {
        // Slot zero pertence aos clientes primários (Afterburner). Nunca captura nem altera slots de outros clientes.
        for (int i = 1; i < count; i++)
        {
            int slot = offset + i * size;
            var owner = memory.Slice(slot + 256, 256);
            if (empty ? owner[0] == 0 : owner[.._owner.Length].SequenceEqual(_owner)) return slot;
        }
        return -1;
    }

    private delegate RtssStatus Operation(Span<byte> memory);
    private unsafe RtssStatus Access(Operation operation)
    {
        if (_disposed) return RtssStatus.Unavailable;
        try
        {
            using var mapping = MemoryMappedFile.OpenExisting(_mappingName, MemoryMappedFileRights.ReadWrite);
            using var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            if (view.Capacity is < 40 or > 134217728) return RtssStatus.Incompatible;
            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                var memory = new Span<byte>(pointer + view.PointerOffset, (int)view.Capacity);
                uint version = Read(memory, 4);
                if (Read(memory, 0) != Signature || version < MinimumVersion || version >= 0x00030000)
                    return RtssStatus.Incompatible;
                ref int busy = ref *(int*)(pointer + view.PointerOffset + 36);
                // Não espera pelo renderer e nunca força o desbloqueio de outro cliente: perde só esta amostra.
                if ((Interlocked.Or(ref busy, 1) & 1) != 0) return RtssStatus.Busy;
                try
                {
                    if (Read(memory, 0) != Signature || !ValidArray(memory.Length, Read(memory, 24), Read(memory, 28),
                            Read(memory, 20), ObjectsOffset, 256)) return RtssStatus.Incompatible;
                    return operation(memory);
                }
                finally { Interlocked.And(ref busy, ~1); }
            }
            finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return RtssStatus.Unavailable; }
    }

    private static bool ValidArray(int capacity, uint offset, uint count, uint size, int minimumSize, uint maximumCount)
        => offset >= 40 && (offset & 3) == 0 && count is > 0 && count <= maximumCount
            && size >= minimumSize && size <= 2_097_152 && (ulong)offset + (ulong)count * size <= (ulong)capacity;
    private static uint Read(Span<byte> memory, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(memory[offset..]);
    private static uint IncrementFrame(Span<byte> memory)
    {
        uint next = unchecked(Read(memory, 32) + 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(memory[32..], next);
        return next;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
    }
}
