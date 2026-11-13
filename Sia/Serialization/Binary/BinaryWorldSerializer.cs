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
    private struct ComponentTraversal<THandler>(THandler handler) : IRefGenericHandler<IHList>
        where THandler : struct, IRefGenericHandler
    {
        private THandler _handler = handler;

        public void Handle<T>(ref T components)
            where T : IHList
        {
            components.HandleHeadRef(ref _handler);
            components.HandleTailRef(ref this);
        }
    }

    // The traversal copies its handlers, so keep the codec and cursor by reference.
    private readonly unsafe struct ComponentWriter<TBufferWriter>(
        TComponentSerializer* serializer, TBufferWriter* writer) : IRefGenericHandler
        where TBufferWriter : IBufferWriter<byte>
    {
        public void Handle<T>(ref T component)
        {
            if (!(*serializer).Serialize(ref *writer, component)) {
                throw new InvalidDataException($"Could not serialize component {typeof(T)}.");
            }
        }
    }

    private readonly unsafe struct ComponentReader(
        TComponentSerializer* serializer, ReadOnlySequence<byte>* buffer,
        Dictionary<EntityId, Entity> entityMap) : IRefGenericHandler
    {
        public void Handle<T>(ref T component)
        {
            if (!(*serializer).Deserialize(ref *buffer, ref component, entityMap)) {
                throw new InvalidDataException($"Could not deserialize component {typeof(T)}.");
            }
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
                var components = new ComponentTraversal<ComponentWriter<TBufferWriter>>(
                    new(&serializer, writerPtr));

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
                        entity.GetHList(components);
                    }
                }
            }
        }
        finally {
            serializer.Dispose();
        }
    }

    [RequiresUnreferencedCode(
        "Calls THostHeaderSerializer.Deserialize, which may resolve serialized types by name.")]
    [RequiresDynamicCode(
        "Calls THostHeaderSerializer.Deserialize, which may construct generic types and methods at runtime.")]
    public static void Deserialize(ref ReadOnlySequence<byte> buffer, World world)
    {
        var serializer = new TComponentSerializer();
        var entityMap = new Dictionary<EntityId, Entity>();
        var serializedIds = new List<EntityId>();
        var loadedEntities = new List<Entity>();

        try {
            while (true) {
                // Commit the input cursor only after a complete host block.
                var hostBlock = buffer;
                var host = THostHeaderSerializer.Deserialize(ref hostBlock, world);
                if (host is null) {
                    buffer = hostBlock;
                    return;
                }

                ReadEntityIds(ref hostBlock, ref serializer, entityMap, serializedIds);
                LoadEntities(ref hostBlock, ref serializer, host, entityMap, serializedIds.AsSpan(), loadedEntities);
                buffer = hostBlock;

                serializedIds.Clear();
                loadedEntities.Clear();
            }
        }
        finally {
            serializer.Dispose();
        }
    }

    private static void ReadEntityIds(
        ref ReadOnlySequence<byte> buffer,
        ref TComponentSerializer serializer,
        Dictionary<EntityId, Entity> entityMap,
        List<EntityId> serializedIds)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (!reader.TryReadBigEndian(out int entityCount) || entityCount < 0) {
            throw new InvalidDataException("Invalid entity count in world data.");
        }

        buffer = reader.UnreadSequence;
        for (var i = 0; i < entityCount; ++i) {
            EntityId id = default;
            if (!serializer.Deserialize(ref buffer, ref id, entityMap) || id.Value == 0) {
                throw new InvalidDataException("Invalid entity ID in world data.");
            }
            if (!entityMap.TryAdd(id, default)) {
                throw new InvalidDataException("Duplicate entity ID in world data.");
            }
            serializedIds.Add(id);
        }
    }

    private static unsafe void LoadEntities(
        ref ReadOnlySequence<byte> buffer,
        ref TComponentSerializer serializer,
        IEntityHost host,
        Dictionary<EntityId, Entity> entityMap,
        ReadOnlySpan<EntityId> serializedIds,
        List<Entity> loadedEntities)
    {
        // A creation callback can throw after publishing the handle.
        Entity pendingEntity = default;
        try {
            loadedEntities.EnsureCapacity(serializedIds.Length);
            foreach (var id in serializedIds) {
                host.CreateForLoad(out pendingEntity);
                if (!ReferenceEquals(pendingEntity.Host, host)) {
                    throw new InvalidOperationException("The entity was released or moved during deserialization.");
                }
                loadedEntities.Add(pendingEntity);
                entityMap[id] = pendingEntity;
                pendingEntity = default;
            }

            // All IDs must point to their entities before a component resolves references.
            fixed (TComponentSerializer* codec = &serializer)
            fixed (ReadOnlySequence<byte>* cursor = &buffer) {
                var components = new ComponentTraversal<ComponentReader>(new(codec, cursor, entityMap));
                foreach (var entity in loadedEntities.AsSpan()) {
                    entity.GetHList(components);
                }
            }
        }
        catch (Exception error) {
            RollBackEntities(Outcome<Exception>.Failure(error), pendingEntity, loadedEntities).ThrowFailure();
        }
    }

    private static Outcome<Exception> RollBackEntities(
        Outcome<Exception> outcome, Entity pendingEntity, List<Entity> loadedEntities)
    {
        if (loadedEntities.Count == 0 || loadedEntities[^1] != pendingEntity) {
            outcome = outcome.Attempt(pendingEntity, ReleaseIfAlive);
        }
        for (var i = loadedEntities.Count - 1; i >= 0; --i) {
            outcome = outcome.Attempt(loadedEntities[i], ReleaseIfAlive);
        }
        return outcome;
    }

    private static void ReleaseIfAlive(in Entity entity)
    {
        if (entity.Host is { } host) {
            host.ReleaseOwned(entity);
        }
    }
}

public class BinaryWorldSerializer
    : BinaryWorldSerializer<ReflectionHostHeaderSerializer, MemoryPackComponentSerializer>;
