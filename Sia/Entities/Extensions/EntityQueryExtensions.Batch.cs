using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sia;

internal interface IArrayBatchHost
{
    bool IsExactArrayHost { get; }
}

public static partial class EntityQueryExtensions
{
    private readonly record struct HostSnapshot(IEntityHost Host, int Start, int Count);

    private ref struct AddBatch<TComponent>(IEntityHost host, ReadOnlySpan<Entity> entities, TComponent initial)
        : IGenericStructTypeHandler<IHList>
    {
        private readonly ReadOnlySpan<Entity> _entities = entities;
        public bool Applied;

        public void Handle<TEntity>() where TEntity : struct, IHList
        {
            if (host is not WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source
                || source.InnerHost.GetType() != typeof(ArrayEntityHost<TEntity>)
                || !CanStartBatch(source, _entities)) return;
            source.World.EnsureOwnerThread();
            if (EntityIndexer<TEntity, TComponent>.Offset != -1) return;
            if (!source.World.TryGetHost<HList<TComponent, TEntity>, ArrayEntityHost<HList<TComponent, TEntity>>>(out var target)) return;
            var dispatcher = source.World.Dispatcher;
            if (source.HasMigrationObservers || target.HasMigrationObservers
                || !dispatcher.HasNoUntypedListeners
                || !dispatcher.HasNoTypedListeners<WorldEvents.Add<TComponent>>()
                || !dispatcher.HasNoTypedListeners<WorldEvents.Set<TComponent>>()) return;
            if (!CanBatch(source, _entities)) return;
            var values = ArrayPool<TComponent>.Shared.Rent(_entities.Length);
            try {
                var span = values.AsSpan(0, _entities.Length);
                span.Fill(initial);
                Applied = source.InnerHost.TryBulkAddAll(source, target, span);
            }
            finally {
                ArrayPool<TComponent>.Shared.Return(values,
                    clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<TComponent>());
            }
        }
    }

    private ref struct RemoveBatch<TComponent>(IEntityHost host, ReadOnlySpan<Entity> entities)
        : IGenericStructTypeHandler<IHList>
    {
        private readonly ReadOnlySpan<Entity> _entities = entities;
        public bool Applied;

        public void Handle<TEntity>() where TEntity : struct, IHList
        {
            if (host is not WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source
                || source.InnerHost.GetType() != typeof(ArrayEntityHost<TEntity>)
                || !CanBatch(source, _entities)
                || !typeof(TEntity).IsGenericType
                || typeof(TEntity).GetGenericTypeDefinition() != typeof(HList<,>)
                || source.Descriptor.Components[0].Type != typeof(TComponent)) return;
            source.World.EnsureOwnerThread();
            var handler = new RemoveHead<TComponent, TEntity>(source);
            default(TEntity).Remove(TypeProxy<TComponent>._, handler);
            Applied = handler.Applied;
        }
    }

    private sealed class RemoveHead<TComponent, TEntity>(WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source)
        : IGenericStructHandler<IHList>
        where TEntity : struct, IHList
    {
        public bool Applied { get; private set; }

        public void Handle<TTail>(in TTail tail) where TTail : struct, IHList
        {
            if (source.World.TryGetHost<TTail, ArrayEntityHost<TTail>>(out var target)) {
                Applied = source.InnerHost.TryBulkRemoveHeadAll<TComponent, TTail>(source, target);
            }
        }
    }

    public static void Add<TComponent>(this IEntityQuery query)
        => query.Add(default(TComponent)!);

    public static void Add<TComponent>(this IEntityQuery query, in TComponent initial)
        => Change(query, initial,
            static (host, entities, value) => {
                if (!MayBatch(host)) return false;
                var batch = new AddBatch<TComponent>(host, entities, value);
                host.Descriptor.HandleType(ref batch);
                return batch.Applied;
            },
            static (entities, value) => { foreach (var entity in entities) entity.Add(value); });

    public static void Remove<TComponent>(this IEntityQuery query)
        => Change(query, default(TComponent),
            static (host, entities, _) => {
                if (!MayBatch(host)) return false;
                var batch = new RemoveBatch<TComponent>(host, entities);
                host.Descriptor.HandleType(ref batch);
                return batch.Applied;
            },
            static (entities, _) => { foreach (var entity in entities) entity.Remove<TComponent>(); });

    private delegate bool TryHostChange<T>(IEntityHost host, ReadOnlySpan<Entity> entities, T value);
    private delegate void EntityChange<T>(ReadOnlySpan<Entity> entities, T value);

    private static bool MayBatch(IEntityHost host)
    {
        if (host is IArrayBatchHost { IsExactArrayHost: true }) return true;
        var type = host.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(WorldEntityHost<,>)) return false;
        var inner = type.GenericTypeArguments[1];
        return inner.IsGenericType && inner.GetGenericTypeDefinition() == typeof(ArrayEntityHost<>);
    }

    private static bool CanStartBatch(IEntityHost host, ReadOnlySpan<Entity> entities)
        => entities.Length != 0 && entities.Length == host.Count
            && entities[0].IsValid && ReferenceEquals(entities[0].Host, host);

    private static bool CanBatch(IEntityHost host, ReadOnlySpan<Entity> entities)
        => entities.Length != 0 && entities.SequenceEqual(host.UnsafeGetEntitySpan())
            && entities[0].IsValid && ReferenceEquals(entities[0].Host, host);

    private static void Change<T>(IEntityQuery query, T value, TryHostChange<T> tryChange, EntityChange<T> change)
    {
        ArgumentNullException.ThrowIfNull(query);
        var hosts = query.Hosts;
        var hostCount = hosts.Count;
        if (hostCount == 0) return;
        var snapshots = ArrayPool<HostSnapshot>.Shared.Rent(hostCount);
        try {
            var count = 0;
            var onlyHost = -1;
            var direct = true;
            for (var i = 0; i < hostCount; i++) {
                var host = hosts[i];
                var length = host.Count;
                snapshots[i] = new(host, count, length);
                count = checked(count + length);
                if (host is not IArrayBatchHost { IsExactArrayHost: true }) direct = false;
                if (length != 0) {
                    if (onlyHost != -1) direct = false;
                    onlyHost = i;
                }
            }
            if (count == 0) return;

            var attempted = false;
            if (direct) {
                var snapshot = snapshots[onlyHost];
                var live = snapshot.Host.UnsafeGetEntitySpan();
                if (live.Length == snapshot.Count) {
                    attempted = true;
                    if (tryChange(snapshot.Host, live, value)) return;
                }
            }

            var entities = ArrayPool<Entity>.Shared.Rent(count);
            try {
                foreach (var snapshot in snapshots.AsSpan(0, hostCount)) {
                    snapshot.Host.UnsafeGetEntitySpan()[..snapshot.Count].CopyTo(entities.AsSpan(snapshot.Start));
                }
                foreach (var snapshot in snapshots.AsSpan(0, hostCount)) {
                    if (snapshot.Count != 0) {
                        var span = entities.AsSpan(snapshot.Start, snapshot.Count);
                        if (attempted || !tryChange(snapshot.Host, span, value)) change(span, value);
                    }
                }
            }
            finally {
                ArrayPool<Entity>.Shared.Return(entities, clearArray: true);
            }
        }
        finally {
            ArrayPool<HostSnapshot>.Shared.Return(snapshots, clearArray: true);
        }
    }
}
