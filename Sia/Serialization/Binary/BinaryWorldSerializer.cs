#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia.Serialization.Binary;

using System.Buffers;
using System.Buffers.Binary;
using CommunityToolkit.HighPerformance;

public class BinaryWorldSerializer<THostHeaderSerializer, TComponentSerializer> : IWorldSerializer
    where THostHeaderSerializer : IHostHeaderSerializer
    where TComponentSerializer : IComponentSerializer, new()
{
    private unsafe struct EntitySerializationHandler<TBufferWriter>(
        TComponentSerializer serializer, TBufferWriter* writer) : IRefGenericHandler<IHList>
        where TBufferWriter : IBufferWriter<byte>
    {
        private unsafe struct HeadHandler(
            TComponentSerializer serializer, TBufferWriter* writer) : IRefGenericHandler
        {
            public void Handle<T>(ref T component)
                => serializer.Serialize(ref *writer, component);
        }

        private HeadHandler _headHandler = new(serializer, writer);

        public void Handle<T>(ref T value)
            where T : IHList
        {
            value.HandleHeadRef(ref _headHandler);
            value.HandleTailRef(ref this);
        }
    }

    private unsafe struct EntityDeserializationHandler(
        TComponentSerializer serializer, ReadOnlySequence<byte>* buffer,
        Dictionary<EntityId, Entity> entityMap)
        : IRefGenericHandler<IHList>
    {
        private unsafe struct HeadHandler(
            TComponentSerializer serializer, ReadOnlySequence<byte>* buffer,
            Dictionary<EntityId, Entity> entityMap)
            : IRefGenericHandler
        {
            public void Handle<T>(ref T component)
                => serializer.Deserialize(ref *buffer, ref component, entityMap);
        }

        private HeadHandler _headHandler = new(serializer, buffer, entityMap);

        public void Handle<T>(ref T value)
            where T : IHList
        {
            value.HandleHeadRef(ref _headHandler);
            value.HandleTailRef(ref this);
        }
    }

    public static unsafe void Serialize<TBufferWriter>(ref TBufferWriter writer, World world)
        where TBufferWriter : IBufferWriter<byte>
    {
        using var serializer = new TComponentSerializer();
        Span<byte> intSpan = stackalloc byte[4];

        fixed (TBufferWriter* writerPtr = &writer) {
            var entitySerializer = new EntitySerializationHandler<TBufferWriter>(serializer, writerPtr);

            foreach (var host in world.Hosts) {
                if (host.Count == 0) {
                    continue;
                }

                THostHeaderSerializer.Serialize(ref writer, host);

                BinaryPrimitives.WriteInt32BigEndian(intSpan, host.Count);
                writer.Write(intSpan);

                foreach (var slot in host.AllocatedSlots) {
                    var entity = host.GetEntity(slot);
                    serializer.Serialize(ref writer, entity.Id);
                }
                foreach (var slot in host.AllocatedSlots) {
                    host.GetHList(slot, entitySerializer);
                }
            }
        }
    }

    public static unsafe void Deserialize(ref ReadOnlySequence<byte> buffer, World world)
    {
        using var serializer = new TComponentSerializer();
        Span<byte> intSpan = stackalloc byte[4];

        var entities = new List<Entity>();
        var entityMap = new Dictionary<EntityId, Entity>();

        fixed (ReadOnlySequence<byte>* bufferPtr = &buffer) {
            var entityDeserializer = new EntityDeserializationHandler(serializer, bufferPtr, entityMap);

            while (true) {
                var host = THostHeaderSerializer.Deserialize(ref buffer, world);
                if (host == null) {
                    break;
                }

                var reader = new SequenceReader<byte>(buffer);
                if (!reader.TryReadBigEndian(out int entityCount)) {
                    break;
                }

                buffer = reader.UnreadSequence;
                var comps = host.Descriptor.Components;

                for (int i = 0; i != entityCount; ++i) {
                    var e = host.Create();
                    EntityId id = default;
                    serializer.Deserialize(ref buffer, ref id, entityMap);
                    entityMap[id] = e;
                    entities.Add(e);
                }

                foreach (var e in entities.AsSpan()) {
                    e.GetHList(entityDeserializer);
                }
                entities.Clear();
            }
        }
    }
}

public class BinaryWorldSerializer
    : BinaryWorldSerializer<ReflectionHostHeaderSerializer, MemoryPackComponentSerializer>;