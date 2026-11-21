using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace CoreBi.Engine.Games;

public enum RtssStatus { Ready, Unavailable, Incompatible, Busy, SlotsFull, ContentTooLarge }
public sealed record RtssContent(string Text, byte[] Objects);
public readonly record struct RtssTarget(bool Active, uint RenderedFrame);

/// <summary>
/// Cliente do protocolo público RTSSSharedMemoryV2. Não carrega DLLs do RTSS nem abre processos de jogos.
/// Cada operação abre a seção existente, permitindo reconectar após reinício do servidor sem manter handles antigos.
/// </summary>
public sealed class RtssOverlayClient : IDisposable
{
    private const uint Signature = 0x52545353;       // "RTSS"
    private const uint MinimumVersion = 0x0002000E;  // v2.14
    private const uint LargeTextVersion = 0x00020014;// v2.20
    private const uint MaximumMajorVersion = 0x00030000;

    // Offsets do cabeçalho RTSS_SHARED_MEMORY (40 bytes)
    private const int HeaderSize = 40;
    private const int OffSignature = 0;
    private const int OffVersion = 4;
    private const int OffAppEntrySize = 8;
    private const int OffAppArrOffset = 12;
    private const int OffAppArrSize = 16;
    private const int OffOsdEntrySize = 20;
    private const int OffOsdArrOffset = 24;
    private const int OffOsdArrSize = 28;
    private const int OffOsdFrame = 32;
    private const int OffBusy = 36;

    // Offsets de RTSS_SHARED_MEMORY_APP_ENTRY e RTSS_SHARED_MEMORY_OSD_ENTRY
    private const int AppMinimumSize = 336;
    private const int OffAppPid = 0;
    private const int OffAppFlags = 264;
    private const int OffAppTime1 = 272;
    private const int OffAppOsdFrame = 332;
    private const int OwnerOffset = 256, OwnerCapacity = 256;
    private const int TextOffset = 512, TextCapacity = 4096;
    private const int ObjectsOffset = 4608, ObjectsCapacity = 262144;
    private const int LargeTextOffset = ObjectsOffset + ObjectsCapacity, LargeTextCapacity = 32768;

    private readonly string _mappingName;
    private readonly byte[] _owner = Encoding.ASCII.GetBytes($"CoreBi:{Environment.ProcessId}:{Guid.NewGuid():N}\0");
    private bool _disposed;
    private bool _ownsSlot;

    public uint PublishedFrame { get; private set; }

    public RtssOverlayClient(string mappingName = "RTSSSharedMemoryV2") => _mappingName = mappingName;

    public RtssStatus Inspect(int processId, out RtssTarget target)
    {
        RtssTarget result = default;
        var status = Access(memory =>
        {
            uint entrySize = ReadUInt32(memory, OffAppEntrySize);
            uint offset = ReadUInt32(memory, OffAppArrOffset);
            uint count = ReadUInt32(memory, OffAppArrSize);
            if (!ValidArray(memory.Length, offset, count, entrySize, AppMinimumSize, 256)) return RtssStatus.Incompatible;
            for (int i = 0; i < count; i++)
            {
                var app = memory.Slice(checked((int)(offset + i * entrySize)), (int)entrySize);
                if (processId <= 0 || ReadUInt32(app, OffAppPid) != (uint)processId) continue;
                // dwTime1 usa GetTickCount (32 bits); subtração modular funciona após wrap de 49,7 dias.
                uint age = unchecked((uint)Environment.TickCount - ReadUInt32(app, OffAppTime1));
                result = new RtssTarget((ReadUInt32(app, OffAppFlags) & 0xFFFF) != 0 && age < 2500, ReadUInt32(app, OffAppOsdFrame));
                break;
            }
            return RtssStatus.Ready;
        });
        target = result;
        return status;
    }

    public RtssStatus Publish(RtssContent content)
    {
        if (content.Text.Contains('\0') || content.Objects.Length > ObjectsCapacity) return RtssStatus.ContentTooLarge;
        // RTSS utiliza texto ANSI; o formatador converte os símbolos fora de Latin-1 antes desta fronteira.
        byte[] text = Encoding.Latin1.GetBytes(content.Text);
        return Access(memory =>
        {
            var (offset, size, count) = ReadOsdLayout(memory);
            bool large = ReadUInt32(memory, OffVersion) >= LargeTextVersion && size >= LargeTextOffset + LargeTextCapacity;
            int capacity = large ? LargeTextCapacity : TextCapacity;
            if (text.Length >= capacity || (content.Objects.Length > 0 && size < ObjectsOffset + ObjectsCapacity))
                return RtssStatus.ContentTooLarge;

            int slot = FindSlot(memory, offset, size, count, empty: false);
            if (slot < 0) slot = FindSlot(memory, offset, size, count, empty: true);
            if (slot < 0) return RtssStatus.SlotsFull;

            var entry = memory.Slice(slot, size);
            _owner.CopyTo(entry[OwnerOffset..]);
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

    public RtssStatus Clear()
    {
        if (!_ownsSlot) return RtssStatus.Ready;
        var status = Access(memory =>
        {
            var (offset, size, count) = ReadOsdLayout(memory);
            int slot = FindSlot(memory, offset, size, count, empty: false);
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

    private static (int Offset, int Size, int Count) ReadOsdLayout(Span<byte> memory) =>
        ((int)ReadUInt32(memory, OffOsdArrOffset), (int)ReadUInt32(memory, OffOsdEntrySize), (int)ReadUInt32(memory, OffOsdArrSize));

    private int FindSlot(Span<byte> memory, int offset, int size, int count, bool empty)
    {
        // Slot zero pertence aos clientes primários (Afterburner). Nunca captura nem altera slots de outros clientes.
        for (int i = 1; i < count; i++)
        {
            int slot = offset + i * size;
            var owner = memory.Slice(slot + OwnerOffset, OwnerCapacity);
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
            if (view.Capacity is < HeaderSize or > 134217728) return RtssStatus.Incompatible;
            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                var memory = new Span<byte>(pointer + view.PointerOffset, (int)view.Capacity);
                uint version = ReadUInt32(memory, OffVersion);
                if (ReadUInt32(memory, OffSignature) != Signature || version < MinimumVersion || version >= MaximumMajorVersion)
                    return RtssStatus.Incompatible;
                ref int busy = ref *(int*)(pointer + view.PointerOffset + OffBusy);
                // Não espera pelo renderer e nunca força o desbloqueio de outro cliente: perde só esta amostra.
                if ((Interlocked.Or(ref busy, 1) & 1) != 0) return RtssStatus.Busy;
                try
                {
                    if (ReadUInt32(memory, OffSignature) != Signature
                        || !ValidArray(memory.Length, ReadUInt32(memory, OffOsdArrOffset), ReadUInt32(memory, OffOsdArrSize),
                                       ReadUInt32(memory, OffOsdEntrySize), ObjectsOffset, 256))
                        return RtssStatus.Incompatible;
                    return operation(memory);
                }
                finally { Interlocked.And(ref busy, ~1); }
            }
            finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return RtssStatus.Unavailable;
        }
    }

    private static bool ValidArray(int capacity, uint offset, uint count, uint size, int minimumSize, uint maximumCount)
        => offset >= HeaderSize && (offset & 3) == 0 && count is > 0 && count <= maximumCount
            && size >= minimumSize && size <= 2_097_152 && (ulong)offset + (ulong)count * size <= (ulong)capacity;

    private static uint ReadUInt32(Span<byte> memory, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(memory[offset..]);

    private static uint IncrementFrame(Span<byte> memory)
    {
        uint next = unchecked(ReadUInt32(memory, OffOsdFrame) + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(memory[OffOsdFrame..], next);
        return next;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
    }
}
