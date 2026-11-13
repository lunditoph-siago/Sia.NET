#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.HighPerformance;

namespace Sia.Serialization.Binary;

public class BinaryWorldSerializer<THostHeaderSerializer, TComponentSerializer> : IWorldSerializer
    where THostHeaderSerializer : IHostHeaderSerializer
    where TComponentSerializer : IComponentSerializer, new()
{
    // HList traversal copies its handlers, so share the operation's codec instance.
    private unsafe struct EntitySerializationHandler<TBufferWriter>(
        TComponentSerializer* serializer, TBufferWriter* writer) : IRefGenericHandler<IHList>
        where TBufferWriter : IBufferWriter<byte>
    {
        private struct HeadHandler(
            TComponentSerializer* serializer, TBufferWriter* writer) : IRefGenericHandler
        {
            public void Handle<T>(ref T component)
            {
                if (!(*serializer).Serialize(ref *writer, component)) {
                    throw new InvalidDataException($"Could not serialize component {typeof(T)}.");
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
        TComponentSerializer* serializer, ReadOnlySequence<byte>* buffer,
        Dictionary<EntityId, Entity> entityMap)
        : IRefGenericHandler<IHList>
    {
        private struct HeadHandler(
            TComponentSerializer* serializer, ReadOnlySequence<byte>* buffer,
            Dictionary<EntityId, Entity> entityMap)
            : IRefGenericHandler
        {
            public void Handle<T>(ref T component)
            {
                if (!(*serializer).Deserialize(ref *buffer, ref component, entityMap)) {
                    throw new InvalidDataException($"Could not deserialize component {typeof(T)}.");
                }
            }
        }

        private HeadHandler _headHandler = new(serializer, buffer, entityMap);

        public void Handle<T>(ref T value)
            where T : IHList
        {
            value.HandleHeadRef(ref _headHandler);
            value.HandleTailRef(ref this);
        }
    }

    [RequiresUnreferencedCode(
        "Calls THostHeaderSerializer.Serialize, which may resolve serialized types by name.")]
    [RequiresDynamicCode(
        "Calls THostHeaderSerializer.Serialize, which may construct generic types and methods at runtime.")]
    public static unsafe void Serialize<TBufferWriter>(ref TBufferWriter writer, World world)
        where TBufferWriter : IBufferWriter<byte>
    {
        var serializer = new TComponentSerializer();
        Span<byte> intSpan = stackalloc byte[4];

        try {
            fixed (TBufferWriter* writerPtr = &writer) {
                var entitySerializer = new EntitySerializationHandler<TBufferWriter>(&serializer, writerPtr);

                foreach (var host in world.Hosts) {
                    if (host.Count == 0) continue;
                    THostHeaderSerializer.Serialize(ref writer, host);

                    BinaryPrimitives.WriteInt32BigEndian(intSpan, host.Count);
                    writer.Write(intSpan);

                    foreach (var entity in host) {
                        if (!serializer.Serialize(ref writer, entity.Id)) {
                            throw new InvalidDataException($"Could not serialize entity ID {entity.Id}.");
                        }
                    }
                    foreach (var entity in host) {
                        entity.GetHList(entitySerializer);
                    }
                }
            }
        }
        finally { serializer.Dispose(); }
    }

    [RequiresUnreferencedCode(
        "Calls THostHeaderSerializer.Deserialize, which may resolve serialized types by name.")]
    [RequiresDynamicCode(
        "Calls THostHeaderSerializer.Deserialize, which may construct generic types and methods at runtime.")]
    public static unsafe void Deserialize(ref ReadOnlySequence<byte> buffer, World world)
    {
        var serializer = new TComponentSerializer();
        var ids = new List<EntityId>();
        var entities = new List<Entity>();
        var entityMap = new Dictionary<EntityId, Entity>();

        try {
            while (true) {
                var block = buffer;
                var host = THostHeaderSerializer.Deserialize(ref block, world);
                if (host is null) {
                    buffer = block;
                    break;
                }
                var reader = new SequenceReader<byte>(block);
                if (!reader.TryReadBigEndian(out int entityCount) || entityCount < 0) {
                    throw new InvalidDataException("Invalid entity count in world data.");
                }
                block = reader.UnreadSequence;
                for (var i = 0; i < entityCount; ++i) {
                    EntityId id = default;
                    if (!serializer.Deserialize(ref block, ref id, entityMap) || id.Value == 0)
                        throw new InvalidDataException("Invalid entity ID in world data.");
                    if (!entityMap.TryAdd(id, default))
                        throw new InvalidDataException("Duplicate entity ID in world data.");
                    ids.Add(id);
                }

                var entityDeserializer = new EntityDeserializationHandler(&serializer, &block, entityMap);
                Entity acquired = default;
                try {
                    entities.EnsureCapacity(entityCount);
                    foreach (var id in ids.AsSpan()) {
                        host.CreateForLoad(out acquired);
                        if (!ReferenceEquals(acquired.Host, host)) {
                            throw new InvalidOperationException("The entity was released or moved during deserialization.");
                        }
                        entities.Add(acquired);
                        entityMap[id] = acquired;
                        acquired = default;
                    }
                    foreach (var entity in entities.AsSpan()) {
                        entity.GetHList(entityDeserializer);
                    }
                }
                catch (Exception error) {
                    var result = Outcome<Exception>.Failure(error);
                    if (acquired.Host is { } owner
                        && (entities.Count == 0 || entities[^1] != acquired)) {
                        result = result.Attempt(() => owner.ReleaseOwned(acquired));
                    }
                    for (var i = entities.Count - 1; i >= 0; i--) {
                        var entity = entities[i];
                        if (entity.IsValid) result = result.Attempt(() => entity.Host.ReleaseOwned(entity));
                    }
                    result.ThrowFailure();
                }
                buffer = block;
                ids.Clear();
                entities.Clear();
            }
        }
        finally { serializer.Dispose(); }
    }
}

public class BinaryWorldSerializer
    : BinaryWorldSerializer<ReflectionHostHeaderSerializer, MemoryPackComponentSerializer>;
