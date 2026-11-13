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
        Span<byte> countBytes = stackalloc byte[4];

        try {
            fixed (TBufferWriter* writerPtr = &writer) {
                var entitySerializer = new EntitySerializationHandler<TBufferWriter>(&serializer, writerPtr);

                foreach (var host in world.Hosts) {
                    if (host.Count == 0) {
                        continue;
                    }

                    THostHeaderSerializer.Serialize(ref writer, host);

                    BinaryPrimitives.WriteInt32BigEndian(countBytes, host.Count);
                    writer.Write(countBytes);

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
        var serializedIds = new List<EntityId>();
        var loadedEntities = new List<Entity>();
        var entityMap = new Dictionary<EntityId, Entity>();

        try {
            while (true) {
                // Commit the input cursor only after a complete host block.
                var hostBlock = buffer;
                var host = THostHeaderSerializer.Deserialize(ref hostBlock, world);
                if (host is null) {
                    buffer = hostBlock;
                    break;
                }
                var reader = new SequenceReader<byte>(hostBlock);
                if (!reader.TryReadBigEndian(out int entityCount) || entityCount < 0) {
                    throw new InvalidDataException("Invalid entity count in world data.");
                }
                hostBlock = reader.UnreadSequence;
                for (var i = 0; i < entityCount; ++i) {
                    EntityId id = default;
                    if (!serializer.Deserialize(ref hostBlock, ref id, entityMap) || id.Value == 0) {
                        throw new InvalidDataException("Invalid entity ID in world data.");
                    }

                    if (!entityMap.TryAdd(id, default)) {
                        throw new InvalidDataException("Duplicate entity ID in world data.");
                    }

                    serializedIds.Add(id);
                }

                var entityDeserializer = new EntityDeserializationHandler(&serializer, &hostBlock, entityMap);
                // A creation callback can throw after the handle has been published.
                Entity pendingEntity = default;
                try {
                    loadedEntities.EnsureCapacity(entityCount);
                    foreach (var id in serializedIds.AsSpan()) {
                        host.CreateForLoad(out pendingEntity);
                        if (!ReferenceEquals(pendingEntity.Host, host)) {
                            throw new InvalidOperationException("The entity was released or moved during deserialization.");
                        }
                        loadedEntities.Add(pendingEntity);
                        entityMap[id] = pendingEntity;
                        pendingEntity = default;
                    }
                    foreach (var entity in loadedEntities.AsSpan()) {
                        entity.GetHList(entityDeserializer);
                    }
                }
                catch (Exception error) {
                    var result = Outcome<Exception>.Failure(error);
                    if (pendingEntity.Host is { } owner
                        && (loadedEntities.Count == 0 || loadedEntities[^1] != pendingEntity)) {
                        result = result.Attempt(() => owner.ReleaseOwned(pendingEntity));
                    }
                    for (var i = loadedEntities.Count - 1; i >= 0; i--) {
                        var entity = loadedEntities[i];
                        if (entity.IsValid) {
                            result = result.Attempt(() => entity.Host.ReleaseOwned(entity));
                        }
                    }
                    result.ThrowFailure();
                }
                buffer = hostBlock;
                serializedIds.Clear();
                loadedEntities.Clear();
            }
        }
        finally { serializer.Dispose(); }
    }
}

public class BinaryWorldSerializer
    : BinaryWorldSerializer<ReflectionHostHeaderSerializer, MemoryPackComponentSerializer>;
