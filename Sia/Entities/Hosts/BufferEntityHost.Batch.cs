using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

namespace Sia;

public partial class BufferEntityHost<TEntity, TBuffer>
    where TEntity : struct, IHList
    where TBuffer : IBuffer<TEntity>
{
    private static class BulkHListShape
    {
        internal static readonly bool IsStandard = Check();

        private static bool Check()
        {
            var type = typeof(TEntity);
            while (type != typeof(EmptyHList)) {
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(HList<,>)) return false;
                type = type.GetGenericArguments()[1];
            }
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanBatchTransition<TDestination>(
        WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source,
        WorldEntityHost<TDestination, ArrayEntityHost<TDestination>> target)
        where TDestination : struct, IHList
    {
        var world = source.World;
        var destination = target.InnerHost;
        return ReferenceEquals(this, source.InnerHost)
            && ReferenceEquals(world, target.World)
            && GetType() == typeof(ArrayEntityHost<TEntity>)
            && destination.GetType() == typeof(ArrayEntityHost<TDestination>)
            && ReferenceEquals(World, world) && ReferenceEquals(destination.World, world)
            && !source.HasMigrationObservers && !target.HasMigrationObservers
            && world.Dispatcher.HasNoUntypedListeners
            && _entities.Count == Buffer.Count
            && destination._entities.Count == destination.Buffer.Count;
    }

    private bool CanMoveAll(IEntityHost source)
    {
        var entities = _entities.AsSpan();
        for (var i = 0; i < entities.Length; i++) {
            var entity = entities[i];
            var state = entity.GetStateUnchecked();
            if (state is null || !entity.References(state) || !ReferenceEquals(state.Host, source)
                || state.Slot != i || state.Releasing || state.EventSink is not null) return false;
        }
        return true;
    }

    private static class BatchHost<THost> where THost : IEntityHost<TEntity>
    {
        private static bool Uses(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type contract,
            string name, Type declaringType)
        {
            try {
                var map = typeof(THost).GetInterfaceMap(contract);
                for (var i = 0; i < map.InterfaceMethods.Length; i++) {
                    if (map.InterfaceMethods[i].Name == name) return map.TargetMethods[i]?.DeclaringType == declaringType;
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidCastException or NotSupportedException or TypeLoadException) {}
            return false;
        }

        private static readonly Type Base = typeof(BufferEntityHost<TEntity, TBuffer>);
        private static readonly bool Common = Uses(typeof(IEntityHost), nameof(IEntityHost.MoveOut), Base)
            && Uses(typeof(IEntityHost), nameof(IEntityHost.UnsafeGetEntitySpan), Base)
            && Uses(typeof(IEntityHost), "get_Count", Base)
            && Uses(typeof(IEntityHost), "get_Descriptor", Base)
            && Uses(typeof(IEntityHost), nameof(IEntityHost.GetSiblingHostType), typeof(ArrayEntityHost<TEntity>));
        public static readonly bool CanAdd = Common && Uses(typeof(IEntityHost), nameof(IEntityHost.Add), Base);
        public static readonly bool CanRemove = Common && Uses(typeof(IEntityHost), nameof(IEntityHost.Remove), Base);
        public static readonly bool DefaultAdd = Uses(typeof(IEntityHost), nameof(IEntityHost.TryAddAll), Base);
        public static readonly bool DefaultRemove = Uses(typeof(IEntityHost), nameof(IEntityHost.TryRemoveAll), Base);
    }

    private bool CanBatch<THost>(WorldEntityHost<TEntity, THost> source, ReadOnlySpan<Entity> entities)
        where THost : IEntityHost<TEntity>, new()
        => ReferenceEquals(this, source.InnerHost) && source.InnerHost.GetType() == typeof(THost)
            && ReferenceEquals(World, source.World) && Buffer is ArrayBuffer<TEntity>
            && entities.Length != 0 && entities.SequenceEqual(_entities.AsSpan())
            && _entities.Count == Buffer.Count && CanMoveAll(source);

    internal bool TryBulkAddUniform<TComponent>(
        WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source, ReadOnlySpan<Entity> entities, in TComponent value)
    {
        source.World.EnsureOwnerThread();
        return TryBatchAddCore(source, entities, value);
    }

    internal bool TryAutoBatchAdd<TComponent, THost>(
        WorldEntityHost<TEntity, THost> source, ReadOnlySpan<Entity> entities, in TComponent initial)
        where THost : IEntityHost<TEntity>, new()
        => RuntimeFeature.IsDynamicCodeSupported && BatchHost<THost>.DefaultAdd && TryBatchAdd(source, entities, initial);

    internal bool UsesDefaultBatchAdd<THost>() where THost : IEntityHost<TEntity>
        => RuntimeFeature.IsDynamicCodeSupported && BatchHost<THost>.DefaultAdd;

    internal bool UsesDefaultBatchRemove<THost>() where THost : IEntityHost<TEntity>
        => RuntimeFeature.IsDynamicCodeSupported && BatchHost<THost>.DefaultRemove;

    internal bool TryAutoBatchRemove<TComponent, THost>(
        WorldEntityHost<TEntity, THost> source, ReadOnlySpan<Entity> entities)
        where THost : IEntityHost<TEntity>, new()
        => RuntimeFeature.IsDynamicCodeSupported && BatchHost<THost>.DefaultRemove && TryBatchRemove(source, entities, TypeProxy<TComponent>._);

    protected bool TryBatchAdd<TComponent, THost>(
        WorldEntityHost<TEntity, THost> source, ReadOnlySpan<Entity> entities, in TComponent initial)
        where THost : IEntityHost<TEntity>, new()
    {
        source.World.EnsureOwnerThread();
        return RuntimeFeature.IsDynamicCodeSupported && BatchHost<THost>.CanAdd
            && TryBatchAddCore(source, entities, initial);
    }

    private bool TryBatchAddCore<TComponent, THost>(
        WorldEntityHost<TEntity, THost> source, ReadOnlySpan<Entity> entities, in TComponent initial)
        where THost : IEntityHost<TEntity>, new()
    {
        if (!CanBatch(source, entities)
            || !BulkHListShape.IsStandard || EntityIndexer<TEntity, TComponent>.Offset != -1
            || !source.World.TryGetHost<HList<TComponent, TEntity>, ArrayEntityHost<HList<TComponent, TEntity>>>(out var target)
            || target.InnerHost.GetType() != typeof(ArrayEntityHost<HList<TComponent, TEntity>>)
            || !ReferenceEquals(target.InnerHost.World, source.World)
            || source.HasMigrationObservers || target.HasMigrationObservers
            || !source.World.Dispatcher.HasNoUntypedListeners
            || !source.World.Dispatcher.HasNoTypedListeners<WorldEvents.Add<TComponent>>()
            || !source.World.Dispatcher.HasNoTypedListeners<WorldEvents.Set<TComponent>>()) return false;
        var destination = (BufferEntityHost<HList<TComponent, TEntity>, ArrayBuffer<HList<TComponent, TEntity>>>)target.InnerHost;
        if (destination._entities.Count != destination.Buffer.Count) return false;
        _ = WorldEvents.Add<TComponent>.Instance;
        _ = EventTypeIndexer<WorldEvents.Add<TComponent>>.Index;
        _ = WorldEvents.Set<TComponent>.Instance;
        _ = EventTypeIndexer<WorldEvents.Set<TComponent>>.Index;
        var value = initial;
        var count = entities.Length;
        var start = destination.Buffer.Count;
        var end = checked(start + count);
        destination._entities.EnsureCapacity(end);
        destination.Buffer.Count = end;
        var sourceBuffer = (ArrayBuffer<TEntity>)(object)Buffer;
        var rows = sourceBuffer.AsSpan();
        var targetRows = destination.Buffer.AsSpan();
        var current = _entities.AsSpan();
        for (var i = 0; i < count; i++) {
            targetRows[start + i] = HList.Cons(value, rows[i]);
            var state = current[i].GetStateUnchecked();
            state.Host = target;
            state.Slot = start + i;
        }
        FinishMoveAll(destination, sourceBuffer, rows, count);
        return true;
    }

    protected bool TryBatchRemove<TComponent, THost>(
        WorldEntityHost<TEntity, THost> source, ReadOnlySpan<Entity> entities, TypeProxy<TComponent> _)
        where THost : IEntityHost<TEntity>, new()
    {
        source.World.EnsureOwnerThread();
        if (!RuntimeFeature.IsDynamicCodeSupported || !BatchHost<THost>.CanRemove || !CanBatch(source, entities)
            || !typeof(TEntity).IsGenericType || typeof(TEntity).GetGenericTypeDefinition() != typeof(HList<,>)
            || source.Descriptor.Components[0].Type != typeof(TComponent)) return false;
        var handler = new BatchRemoveHead<TComponent, THost>(this, source);
        default(TEntity).Remove(TypeProxy<TComponent>._, handler);
        return handler.Applied;
    }

    private sealed class BatchRemoveHead<TComponent, THost>(
        BufferEntityHost<TEntity, TBuffer> host, WorldEntityHost<TEntity, THost> source)
        : IGenericStructHandler<IHList> where THost : IEntityHost<TEntity>, new()
    {
        public bool Applied;
        public void Handle<TTail>(in TTail tail) where TTail : struct, IHList
        {
            if (source.World.TryGetHost<TTail, ArrayEntityHost<TTail>>(out var target))
                Applied = host.TryBatchRemoveHead<TComponent, TTail, THost>(source, target);
        }
    }

    private bool TryBatchRemoveHead<THead, TTail, THost>(WorldEntityHost<TEntity, THost> source,
        WorldEntityHost<TTail, ArrayEntityHost<TTail>> target)
        where TTail : struct, IHList where THost : IEntityHost<TEntity>, new()
    {
        var world = source.World;
        var destination = (BufferEntityHost<TTail, ArrayBuffer<TTail>>)target.InnerHost;
        if (typeof(TEntity) != typeof(HList<THead, TTail>)
            || target.InnerHost.GetType() != typeof(ArrayEntityHost<TTail>)
            || !ReferenceEquals(destination.World, world) || source.HasMigrationObservers || target.HasMigrationObservers
            || !world.Dispatcher.HasNoUntypedListeners
            || !world.Dispatcher.HasNoTypedListeners<WorldEvents.Remove<THead>>()
            || destination._entities.Count != destination.Buffer.Count) return false;
        _ = WorldEvents.Remove<THead>.Instance;
        _ = EventTypeIndexer<WorldEvents.Remove<THead>>.Index;
        var count = _entities.Count;
        var start = destination.Buffer.Count;
        var end = checked(start + count);
        destination._entities.EnsureCapacity(end);
        destination.Buffer.Count = end;
        var sourceBuffer = (ArrayBuffer<TEntity>)(object)Buffer;
        var rows = sourceBuffer.AsSpan();
        var targetRows = destination.Buffer.AsSpan();
        var entities = _entities.AsSpan();
        for (var i = 0; i < count; i++) {
            targetRows[start + i] = Unsafe.As<TEntity, HList<THead, TTail>>(ref rows[i]).Tail;
            var state = entities[i].GetStateUnchecked();
            state.Host = target;
            state.Slot = start + i;
        }
        FinishMoveAll(destination, sourceBuffer, rows, count);
        return true;
    }

    private void FinishMoveAll<TDestination>(
        BufferEntityHost<TDestination, ArrayBuffer<TDestination>> destination,
        ArrayBuffer<TEntity> sourceBuffer, Span<TEntity> sourceRows, int count)
        where TDestination : struct, IHList
    {
        destination._entities.AddRange(_entities);
        Version += count;
        destination.Version += count;
        sourceRows[..count].Clear();
        _entities.Clear();
        sourceBuffer.Count = 0;
    }

    internal bool TryBulkAddAll<TComponent>(
        WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source,
        WorldEntityHost<HList<TComponent, TEntity>, ArrayEntityHost<HList<TComponent, TEntity>>> target,
        ReadOnlySpan<TComponent> initialValues)
    {
        var world = source.World;
        world.EnsureOwnerThread();
        var destination = target.InnerHost;
        if (!CanBatchTransition(source, target) || Buffer is not ArrayBuffer<TEntity> sourceBuffer
            || !BulkHListShape.IsStandard || EntityIndexer<TEntity, TComponent>.Offset != -1) return false;
        var count = _entities.Count;
        if (initialValues.Length != count) return false;
        if (count == 0) return true;
        if (!world.Dispatcher.HasNoTypedListeners<WorldEvents.Add<TComponent>>()
            || !world.Dispatcher.HasNoTypedListeners<WorldEvents.Set<TComponent>>()) return false;
        if (!CanMoveAll(source)) return false;
        _ = WorldEvents.Add<TComponent>.Instance;
        world.EnsureOwnerThread();
        _ = EventTypeIndexer<WorldEvents.Add<TComponent>>.Index;
        _ = WorldEvents.Set<TComponent>.Instance;
        world.EnsureOwnerThread();
        _ = EventTypeIndexer<WorldEvents.Set<TComponent>>.Index;
        var start = destination.Buffer.Count;
        var end = checked(start + count);
        destination._entities.EnsureCapacity(end);
        destination.Buffer.Count = end;
        var sourceRows = sourceBuffer.AsSpan();
        var targetRows = destination.Buffer.AsSpan();
        var entities = _entities.AsSpan();
        for (var i = 0; i < count; i++) {
            targetRows[start + i] = HList.Cons(initialValues[i], sourceRows[i]);
            var state = entities[i].GetStateUnchecked();
            state.Host = target;
            state.Slot = start + i;
        }
        FinishMoveAll(destination, sourceBuffer, sourceRows, count);
        return true;
    }

    internal bool TryBulkRemoveHeadAll<THead, TTail>(
        WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source,
        WorldEntityHost<TTail, ArrayEntityHost<TTail>> target)
        where TTail : struct, IHList
    {
        var world = source.World;
        world.EnsureOwnerThread();
        var destination = target.InnerHost;
        if (typeof(TEntity) != typeof(HList<THead, TTail>)
            || !CanBatchTransition(source, target) || Buffer is not ArrayBuffer<TEntity> sourceBuffer) return false;
        var count = _entities.Count;
        if (count == 0) return true;
        if (!world.Dispatcher.HasNoTypedListeners<WorldEvents.Remove<THead>>()) return false;
        if (!CanMoveAll(source)) return false;
        _ = WorldEvents.Remove<THead>.Instance;
        world.EnsureOwnerThread();
        _ = EventTypeIndexer<WorldEvents.Remove<THead>>.Index;
        var start = destination.Buffer.Count;
        var end = checked(start + count);
        destination._entities.EnsureCapacity(end);
        destination.Buffer.Count = end;
        var sourceRows = sourceBuffer.AsSpan();
        var targetRows = destination.Buffer.AsSpan();
        var entities = _entities.AsSpan();
        for (var i = 0; i < count; i++) {
            targetRows[start + i] = Unsafe.As<TEntity, HList<THead, TTail>>(ref sourceRows[i]).Tail;
            var state = entities[i].GetStateUnchecked();
            state.Host = target;
            state.Slot = start + i;
        }
        FinishMoveAll(destination, sourceBuffer, sourceRows, count);
        return true;
    }

}
