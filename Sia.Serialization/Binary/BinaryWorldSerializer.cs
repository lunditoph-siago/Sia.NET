#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia.Serialization.Binary;

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class BinaryWorldSerializer<THostHeaderSerializer, TComponentSerializer> : IWorldSerializer
    where THostHeaderSerializer : IHostHeaderSerializer
    where TComponentSerializer : IComponentSerializer, new()
{
    private unsafe struct EntitySerializationHandler<TBufferWriter>(
        TComponentSerializer serializer, TBufferWriter* writer) : IRefGenericHandler<IHList>
        where TBufferWriter : IBufferWriter<byte>
    {
        private unsafe struct HeadHandler(
            TComponentSerializer serializer, TBufferWriter* writer) : IGenericHandler
        {
            public void Handle<T>(in T value)
                => serializer.Serialize(ref *writer, value);
        }

        private HeadHandler _headHandler = new(serializer, writer);

        public readonly void Handle<T>(ref T value)
            where T : IHList
        {
            value.HandleHead(_headHandler);
            value.HandleTailRef(this);
        }
    }

    private unsafe struct EntityDeserializationHandler(
        TComponentSerializer serializer, ReadOnlySequence<byte>* buffer) : IRefGenericHandler<IHList>
    {
        private unsafe struct HeadHandler(
            TComponentSerializer serializer, ReadOnlySequence<byte>* buffer) : IRefGenericHandler
        {
            private readonly Dictionary<Identity, Identity> _idMap = [];

            public void Handle<T>(ref T component)
            {
                serializer.Deserialize(ref *buffer, ref component);
                if (component is IRelation || component is Identity) {
                    ref var target = ref Unsafe.As<T, Identity>(ref component);
                    ref var mapped = ref CollectionsMarshal.GetValueRefOrAddDefault(_idMap, target, out bool exists);
                    if (!exists) {
                        mapped = Identity.Create();
                    }
                    target = mapped;
                }
            }
        }

        private HeadHandler _headHandler = new(serializer, buffer);

        public readonly void Handle<T>(ref T value)
            where T : IHList
        {
            value.HandleHeadRef(_headHandler);
            value.HandleTailRef(this);
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

        fixed (ReadOnlySequence<byte>* bufferPtr = &buffer) {
            var entityDeserializer = new EntityDeserializationHandler(serializer, bufferPtr);

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
                for (int i = 0; i != entityCount; ++i) {
                    host.Create().GetHList(entityDeserializer);
                }
            }
        }
    }
}

public class BinaryWorldSerializer
    : BinaryWorldSerializer<ReflectionHostHeaderSerializer, MemoryPackComponentSerializer>;