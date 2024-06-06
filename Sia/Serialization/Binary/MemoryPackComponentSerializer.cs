namespace Sia.Serialization.Binary;

using System.Buffers;
using System.Runtime.CompilerServices;
using MemoryPack;

public struct MemoryPackComponentSerializer() : IComponentSerializer
{
    private MemoryPackWriterOptionalState? _writerState;
    private MemoryPackReaderOptionalState? _readerState;
    private Dictionary<EntityId, Entity>? _entityMap;

    private record DeserializerServiceProvider(Dictionary<EntityId, Entity> EntityMap)
        : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private class EntityFormatter : MemoryPackFormatter<Entity>
    {
        public override void Serialize<TBufferWriter>(
            ref MemoryPackWriter<TBufferWriter> writer, scoped ref Entity? value)
            => writer.WriteValue(value != null ? value.Id.Value : 0);

        public unsafe override void Deserialize(ref MemoryPackReader reader, scoped ref Entity? value)
        {
            var map = ((DeserializerServiceProvider)reader.Options.ServiceProvider!).EntityMap;
            int id = reader.ReadValue<int>();
            if (id == 0) {
                value = null;
                return;
            }
            value = map.TryGetValue(new(id), out var entity) ? entity : null;
        }
    }

    static MemoryPackComponentSerializer()
    {
        MemoryPackFormatterProvider.Register(new EntityFormatter());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Serialize<TBufferWriter, TComponent>(
        ref TBufferWriter writer, in TComponent component)
        where TBufferWriter : IBufferWriter<byte>
    {
        _writerState ??= MemoryPackWriterOptionalStatePool.Rent(MemoryPackSerializerOptions.Default);

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
        ref ReadOnlySequence<byte> buffer, ref TComponent component, Dictionary<EntityId, Entity> entityMap)
    {
        if (_readerState == null || _entityMap != entityMap) {
            _readerState = MemoryPackReaderOptionalStatePool.Rent(
                MemoryPackSerializerOptions.Default with {
                    ServiceProvider = new DeserializerServiceProvider(entityMap)
                });
            _entityMap = entityMap;
        }

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
        ((IDisposable?)_writerState)?.Dispose();
        ((IDisposable?)_readerState)?.Dispose();

        _writerState = null;
        _readerState = null;
        _entityMap = null;
    }
}