#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia.Serialization.Binary;

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public class BinaryWorldSerializer<THostHeaderSerializer, TComponentSerializer> : IWorldSerializer
    where THostHeaderSerializer : IHostHeaderSerializer
    where TComponentSerializer : IComponentSerializer, new()
{
    private unsafe struct GenericSerializer<TBufferWriter>(
        TComponentSerializer serializer, TBufferWriter* writer) : IRefGenericHandler
        where TBufferWriter : IBufferWriter<byte>
    {
        public void Handle<T>(ref T value)
            => serializer.Serialize(ref *writer, value);
    }

    private unsafe struct GenericDeserializer(
        TComponentSerializer serializer, ReadOnlySequence<byte>* sequence) : IRefGenericHandler
    {
        public void Handle<T>(ref T value)
            => serializer.Deserialize(ref *sequence, ref value);
    }

    private unsafe struct EntitySerializationHandler<TBufferWriter>(
        TComponentSerializer serializer, TBufferWriter* writer) : IRefGenericHandler<IHList>
        where TBufferWriter : IBufferWriter<byte>
    {
        private unsafe struct HeadHandler(
            TComponentSerializer serializer, TBufferWriter* writer) : IRefGenericHandler
        {
            public void Handle<T>(ref T component)
            {
                var t = typeof(T);
                if (t == typeof(Entity) || component is IRelationComponent) {
                    serializer.Serialize(ref *writer, Unsafe.As<T, Entity>(ref component).Id);
                    if (component is IArgumentRelationComponent argComp) {
                        argComp.HandleRelation(new GenericSerializer<TBufferWriter>(serializer, writer));
                    }
                }
                else {
                    serializer.Serialize(ref *writer, component);
                }
            }
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
        Dictionary<long, Entity> idMap, List<(Entity, long, int)> relations) : IRefGenericHandler<IHList>
    {
        private unsafe struct HeadHandler(
            TComponentSerializer serializer, ReadOnlySequence<byte>* buffer,
            Dictionary<long, Entity> idMap, List<(Entity, long, int)> relations) : IRefGenericHandler
        {
            private Entity? entity;
            private int counter;

            public void Handle<T>(ref T component)
            {
                var t = typeof(T);
                if (t == typeof(Entity)) {
                    entity = Unsafe.As<T, Entity>(ref component);
                    long id = 0;
                    serializer.Deserialize(ref *buffer, ref id);
                    idMap[id] = entity;
                }
                else if (component is IRelationComponent) {
                    if (entity == null) {
                        throw new InvalidDataException("Entity component must appear before any relation component");
                    }

                    long id = 0;
                    serializer.Deserialize(ref *buffer, ref id);
                    relations.Add((entity, id, counter));

                    if (component is IArgumentRelationComponent argComp) {
                        argComp.HandleRelation(new GenericDeserializer(serializer, buffer));
                        component = (T)argComp;
                    }
                }
                else {
                    serializer.Deserialize(ref *buffer, ref component);
                }
                counter++;
            }
        }

        private HeadHandler _headHandler = new(serializer, buffer, idMap, relations);

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
                    host.GetHList(slot, entitySerializer);
                }
            }
        }
    }

    public static unsafe void Deserialize(ref ReadOnlySequence<byte> buffer, World world)
    {
        using var serializer = new TComponentSerializer();
        Span<byte> intSpan = stackalloc byte[4];

        var idMap = new Dictionary<long, Entity>();
        var relations = new List<(Entity, long, int)>();

        fixed (ReadOnlySequence<byte>* bufferPtr = &buffer) {
            var entityDeserializer = new EntityDeserializationHandler(serializer, bufferPtr, idMap, relations);

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
                    e.GetHList(entityDeserializer);
                }
            }
        }

        foreach (var (e, id, index) in relations.AsSpan()) {
            var comps = e.Descriptor.Components;
            ref var byteRef = ref Unsafe.AddByteOffset(ref e.AsSpan()[0], comps[index].Offset);
            Unsafe.As<byte, Entity>(ref byteRef) = idMap[id];
        }
    }
}

public class BinaryWorldSerializer
    : BinaryWorldSerializer<ReflectionHostHeaderSerializer, MemoryPackComponentSerializer>;