using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sia;

internal static class EntityBatch
{
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
            if (RuntimeFeature.IsDynamicCodeSupported
                && source.InnerHost.TryBulkAddUniform(source, _entities, initial)) {
                Applied = true;
                return;
            }
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

    internal static bool TryAdd<TComponent>(IEntityHost host, ReadOnlySpan<Entity> entities, TComponent value)
    {
        if (!MayBatch(host)) return false;
        var batch = new AddBatch<TComponent>(host, entities, value);
        host.Descriptor.HandleType(ref batch);
        return batch.Applied;
    }

    internal static bool TryRemove<TComponent>(IEntityHost host, ReadOnlySpan<Entity> entities)
    {
        if (!MayBatch(host)) return false;
        var batch = new RemoveBatch<TComponent>(host, entities);
        host.Descriptor.HandleType(ref batch);
        return batch.Applied;
    }

    private static bool MayBatch(IEntityHost host)
    {
        if (host is IWorldBatchHost { IsExactArrayHost: true }) return true;
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
}
