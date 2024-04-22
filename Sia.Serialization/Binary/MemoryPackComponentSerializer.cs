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
    public void Serialize<TBufferWriter, TComponent>(
        ref TBufferWriter writer, in TComponent component) where TBufferWriter : IBufferWriter<byte>
    {
        var mpWriter = new MemoryPackWriter<TBufferWriter>(ref writer, _writerState);
        MemoryPackSerializer.Serialize(ref mpWriter, component);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deserialize<TComponent>(
        ref ReadOnlySequence<byte> buffer, ref TComponent component)
    {
        var mpReader = new MemoryPackReader(buffer, _readerState);
        mpReader.ReadValue(ref component!);
        buffer = buffer.Slice(mpReader.Consumed);
    }

    public void Dispose()
    {
        ((IDisposable)_writerState).Dispose();
        ((IDisposable)_readerState).Dispose();
    }
}