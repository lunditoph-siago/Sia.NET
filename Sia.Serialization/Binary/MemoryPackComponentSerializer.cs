namespace Sia.Serialization.Binary;

using System.Buffers;
using System.Runtime.CompilerServices;
using MemoryPack;

public readonly struct MemoryPackComponentSerializer() : IComponentSerializer
{
    private readonly MemoryPackWriterOptionalState _writerState =
        MemoryPackWriterOptionalStatePool.Rent(MemoryPackSerializerOptions.Default);
        
    private readonly MemoryPackReaderOptionalState _readerState =
        MemoryPackReaderOptionalStatePool.Rent(MemoryPackSerializerOptions.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Serialize<TBufferWriter, TComponent>(
        ref TBufferWriter writer, in TComponent component) where TBufferWriter : IBufferWriter<byte>
    {
        var mpWriter = new MemoryPackWriter<TBufferWriter>(ref writer, _writerState);
        try {
            MemoryPackSerializer.Serialize(ref mpWriter, component);
            return true;
        }
        catch (MemoryPackSerializationException) {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Deserialize<TComponent>(
        ref ReadOnlySequence<byte> buffer, ref TComponent component)
    {
        var mpReader = new MemoryPackReader(buffer, _readerState);
        try {
            mpReader.ReadValue(ref component!);
            buffer = buffer.Slice(mpReader.Consumed);
            return true;
        }
        catch (MemoryPackSerializationException) {
            return false;
        }
    }

    public void Dispose()
    {
        ((IDisposable)_writerState).Dispose();
        ((IDisposable)_readerState).Dispose();
    }
}