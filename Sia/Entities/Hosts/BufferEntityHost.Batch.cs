using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

namespace Sia;

public partial class BufferEntityHost<TEntity, TBuffer>
    where TEntity : struct, IHList
    where TBuffer : IBuffer<TEntity>
{
    private static class HListShape<TList>
    {
        internal static readonly bool IsStandard = Check();

        private static bool Check()
        {
            var type = typeof(TList);
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
            && !target.IsClosed
            && !source.HasMigrationObservers && !target.HasMigrationObservers
            && world.Dispatcher.HasNoUntypedListeners
            && _entities.Count == Storage.Count
            && destination._entities.Count == destination.Storage.Count;
    }

    private bool CanMoveAll(IEntityHost source)
    {
        var entities = _entities.AsSpan();
        for (var i = 0; i < entities.Length; i++) {
            var entity = entities[i];
            var state = entity.GetStateUnchecked();
            if (state is null || !entity.References(state) || !ReferenceEquals(state.Host, source)
                || state.Slot != i || state.IsTransitioning || state.EventSink is not null) return false;
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
            && ReferenceEquals(World, source.World) && Storage is ArrayBuffer<TEntity>
            && entities.Length != 0 && entities.SequenceEqual(_entities.AsSpan())
            && _entities.Count == Storage.Count && CanMoveAll(source);

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
            || !HListShape<TEntity>.IsStandard || EntityIndexer<TEntity, TComponent>.Offset != -1
            || !source.World.TryGetHost<HList<TComponent, TEntity>, ArrayEntityHost<HList<TComponent, TEntity>>>(out var target)
            || target.InnerHost.GetType() != typeof(ArrayEntityHost<HList<TComponent, TEntity>>)
            || !ReferenceEquals(target.InnerHost.World, source.World)
            || target.IsClosed
            || source.HasMigrationObservers || target.HasMigrationObservers
            || !source.World.Dispatcher.HasNoUntypedListeners
            || !source.World.Dispatcher.HasNoTypedListeners<WorldEvents.Add<TComponent>>()
            || !source.World.Dispatcher.HasNoTypedListeners<WorldEvents.Set<TComponent>>()) return false;
        var destination = (BufferEntityHost<HList<TComponent, TEntity>, ArrayBuffer<HList<TComponent, TEntity>>>)target.InnerHost;
        if (destination._entities.Count != destination.Storage.Count) return false;
        _ = WorldEvents.Add<TComponent>.Instance;
        _ = EventTypeIndexer<WorldEvents.Add<TComponent>>.Index;
        _ = WorldEvents.Set<TComponent>.Instance;
        _ = EventTypeIndexer<WorldEvents.Set<TComponent>>.Index;
        var sourceBuffer = (ArrayBuffer<TEntity>)(object)Storage;
        MoveWithHead(sourceBuffer, destination, target, entities.Length, initial, default);
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
            var world = source.World;
            if (!world.TryGetHost<TTail, ArrayEntityHost<TTail>>(out var target)) return;

            var destination = target.InnerHost;
            if (typeof(TEntity) != typeof(HList<TComponent, TTail>)
                || destination.GetType() != typeof(ArrayEntityHost<TTail>)
                || target.IsClosed
                || !ReferenceEquals(destination.World, world) || source.HasMigrationObservers || target.HasMigrationObservers
                || !world.Dispatcher.HasNoUntypedListeners
                || !world.Dispatcher.HasNoTypedListeners<WorldEvents.Remove<TComponent>>()
                || destination._entities.Count != destination.Storage.Count) {
                Applied = false;
                return;
            }
            _ = WorldEvents.Remove<TComponent>.Instance;
            _ = EventTypeIndexer<WorldEvents.Remove<TComponent>>.Index;
            var sourceBuffer = (ArrayBuffer<TEntity>)(object)host.Storage;
            host.MoveWithoutHead<TComponent, TTail>(sourceBuffer, destination, target, host._entities.Count);
            Applied = true;
        }
    }

    private readonly struct BatchMove<TDestination>(BufferEntityHost<TEntity, TBuffer> source,
        BufferEntityHost<TDestination, ArrayBuffer<TDestination>> destination, IEntityHost target, int start, int count)
        where TDestination : struct, IHList
    {
        public readonly BufferEntityHost<TEntity, TBuffer> Source = source;
        public readonly BufferEntityHost<TDestination, ArrayBuffer<TDestination>> Destination = destination;
        public readonly IEntityHost Target = target;
        public readonly int Start = start;
        public readonly int Count = count;
    }

    private readonly ref struct BatchAdd<TComponent>(BatchMove<HList<TComponent, TEntity>> move,
        in TComponent initial, ReadOnlySpan<TComponent> initialValues)
    {
        public readonly BatchMove<HList<TComponent, TEntity>> Move = move;
        public readonly TComponent Initial = initial;
        public readonly ReadOnlySpan<TComponent> InitialValues = initialValues;
    }

    private BatchMove<TDestination> PrepareMoveAll<TDestination>(
        BufferEntityHost<TDestination, ArrayBuffer<TDestination>> destination, IEntityHost target, int count)
        where TDestination : struct, IHList
    {
        var start = destination.Storage.Count;
        var end = checked(start + count);
        destination._entities.EnsureCapacity(end);
        destination.Storage.Count = end;
        return new(this, destination, target, start, count);
    }

    private void MoveWithHead<TComponent>(ArrayBuffer<TEntity> sourceBuffer,
        BufferEntityHost<HList<TComponent, TEntity>, ArrayBuffer<HList<TComponent, TEntity>>> destination,
        IEntityHost target, int count, in TComponent initial, ReadOnlySpan<TComponent> initialValues)
    {
        var value = initial;
        var move = PrepareMoveAll(destination, target, count);
        var handlerState = new BatchAdd<TComponent>(move, value, initialValues);
        sourceBuffer.HandleRows(destination.Storage, ref handlerState, static (
            scoped Span<TEntity> sourceRows, scoped Span<HList<TComponent, TEntity>> targetRows,
            scoped ref BatchAdd<TComponent> batch) => {
            var transfer = batch.Move;
            var entities = transfer.Source._entities.AsSpan();
            var values = batch.InitialValues;
            var uniform = values.IsEmpty;
            for (var i = 0; i < transfer.Count; i++) {
                var slot = transfer.Start + i;
                targetRows[slot] = uniform
                    ? HList.Cons(batch.Initial, sourceRows[i])
                    : HList.Cons(values[i], sourceRows[i]);
                var state = entities[i].GetStateUnchecked();
                state.Host = transfer.Target;
                state.Slot = slot;
            }
            transfer.Source.FinishMoveAll(transfer.Destination, sourceRows, transfer.Count);
        });
    }

    private void MoveWithoutHead<THead, TTail>(ArrayBuffer<TEntity> sourceBuffer,
        BufferEntityHost<TTail, ArrayBuffer<TTail>> destination, IEntityHost target, int count)
        where TTail : struct, IHList
    {
        var handlerState = PrepareMoveAll(destination, target, count);
        sourceBuffer.HandleRows(destination.Storage, ref handlerState, static (
            scoped Span<TEntity> sourceRows, scoped Span<TTail> targetRows, scoped ref BatchMove<TTail> move) => {
            var entities = move.Source._entities.AsSpan();
            for (var i = 0; i < move.Count; i++) {
                var slot = move.Start + i;
                targetRows[slot] = Unsafe.As<TEntity, HList<THead, TTail>>(ref sourceRows[i]).Tail;
                var state = entities[i].GetStateUnchecked();
                state.Host = move.Target;
                state.Slot = slot;
            }
            move.Source.FinishMoveAll(move.Destination, sourceRows, move.Count);
        });
    }

    private void FinishMoveAll<TDestination>(
        BufferEntityHost<TDestination, ArrayBuffer<TDestination>> destination, scoped Span<TEntity> sourceRows, int count)
        where TDestination : struct, IHList
    {
        destination._entities.AddRange(_entities);
        Version += count;
        destination.Version += count;
        sourceRows[..count].Clear();
        _entities.Clear();
        Storage.Count = 0;
    }

    internal bool TryBulkAddAll<TComponent>(
        WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source,
        WorldEntityHost<HList<TComponent, TEntity>, ArrayEntityHost<HList<TComponent, TEntity>>> target,
        ReadOnlySpan<TComponent> initialValues)
    {
        var world = source.World;
        world.EnsureOwnerThread();
        var destination = target.InnerHost;
        if (!CanBatchTransition(source, target) || Storage is not ArrayBuffer<TEntity> sourceBuffer
            || !HListShape<TEntity>.IsStandard || EntityIndexer<TEntity, TComponent>.Offset != -1) return false;
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
        MoveWithHead(sourceBuffer, destination, target, count, default!, initialValues);
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
            || !CanBatchTransition(source, target) || Storage is not ArrayBuffer<TEntity> sourceBuffer) return false;
        var count = _entities.Count;
        if (count == 0) return true;
        if (!world.Dispatcher.HasNoTypedListeners<WorldEvents.Remove<THead>>()) return false;
        if (!CanMoveAll(source)) return false;
        _ = WorldEvents.Remove<THead>.Instance;
        world.EnsureOwnerThread();
        _ = EventTypeIndexer<WorldEvents.Remove<THead>>.Index;
        MoveWithoutHead<THead, TTail>(sourceBuffer, destination, target, count);
        return true;
    }

}
