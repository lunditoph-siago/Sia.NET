using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;

namespace Sia;

public static class BufferEntityHost<TEntity>
    where TEntity : struct, IHList
{
    public static BufferEntityHost<TEntity, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<TEntity>
        => new(buffer);
}

public partial class BufferEntityHost<TEntity, TBuffer>(TBuffer buffer)
    : IEntityHost<TEntity>
    where TEntity : struct, IHList
    where TBuffer : IBuffer<TEntity>
{
    public event Action<IEntityHost>? OnDisposed;

    public Type EntityType => typeof(TEntity);
    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<TEntity>();

    public World? World { get; private set; }

    public int Capacity => Buffer.Capacity;
    public int Count => Buffer.Count;
    public int Version { get; private set; }

    public bool TryGetSequentialBytes(out Span<byte> bytes)
    {
        var count = Buffer.Count;
        if (count == 0
            || RuntimeHelpers.IsReferenceOrContainsReferences<TEntity>()
            || (long)count * Unsafe.SizeOf<TEntity>() > int.MaxValue) {
            bytes = default;
            return false;
        }
        bytes = MemoryMarshal.Cast<TEntity, byte>(Buffer.AsSpan()[..count]);
        return true;
    }

    public TBuffer Buffer { get; } = buffer;

    private readonly List<Entity> _entities = [];
    private EntityStatePool? _entityStates;
    private bool _ownsEntityStates = true;
    private bool _disposed;

    private EntityStatePool EntityStates {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _entityStates ??= new();
    }

    void IEntityHost.OnInitialize(World world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Count != 0) {
            throw new InvalidOperationException(
                "Cannot initialize a non-empty host.");
        }
        if (_ownsEntityStates) {
            _entityStates?.Retire();
        }
        _entityStates = world.EntityStates;
        _ownsEntityStates = false;
        World = world;
    }

    public virtual Entity Create() => Create(default!);
    public virtual Entity Create(in TEntity initial)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (typeof(TBuffer) == typeof(ArrayBuffer<TEntity>)) {
            var buffer = (ArrayBuffer<TEntity>)(object)Buffer;
            ReserveArrayRow(buffer);
            var entity = EntityStates.Rent();
            AppendReservedArrayRow(buffer, entity, entity.GetStateUnchecked(), this, initial);
            return entity;
        }
        return CreateWithRollback(initial);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Entity CreateWithRollback(in TEntity initial)
    {
        var states = EntityStates;
        var entity = states.Rent();
        try {
            AppendRow(entity, initial);
            return entity;
        }
        catch (Exception error) {
            return Outcome<Exception>.Failure(error)
                .Attempt(() => states.Return(entity.GetStateUnchecked()))
                .ThrowFailure<Entity>();
        }
    }

    public virtual void Release(Entity entity)
    {
        var state = entity.GetState();
        state.EnsureOutsideMigrationCallback();
        if (state.Migrating) {
            throw new InvalidOperationException("Cannot release an entity while it is being migrated.");
        }
        EnsureOwnSlot(entity, state);
        RemoveRow(state);
        EntityStates.Return(state);
    }

    public Entity GetEntity(int slot)
        => _entities[slot];

    public void MoveOut(Entity entity, IEntityHost destination)
    {
        var state = entity.GetState();
        state.EnsureOutsideMigrationCallback();
        if (state.Releasing) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        EnsureOwnSlot(entity, state);
        if (!ReferenceEquals(destination.World, World)) {
            throw new InvalidOperationException("Migration destination belongs to another world.");
        }
        destination.EnsureCanMoveIn();
        RemoveRow(state);
    }

    void IEntityHost.EnsureCanMoveIn() => ObjectDisposedException.ThrowIf(_disposed, this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureOwnSlot(Entity entity, EntityState state)
    {
        if ((uint)state.Slot >= (uint)_entities.Count || _entities[state.Slot] != entity) {
            throw new InvalidOperationException("Entity belongs to another host.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveRow(EntityState state)
    {
        Version++;

        var slot = state.Slot;
        var lastSlot = Buffer.Count - 1;

        if (slot != lastSlot) {
            Buffer.GetRef(slot) = Buffer.GetRef(lastSlot);
            var lastEntity = _entities[lastSlot];
            lastEntity.GetStateUnchecked().Slot = slot;
            _entities[slot] = lastEntity;
        }
        Buffer.GetRef(lastSlot) = default!;
        _entities.RemoveAt(lastSlot);
        Buffer.Count--;
    }

    public void MoveIn(Entity entity, in TEntity data, IEntityHost source)
    {
        var state = entity.GetCurrentState();
        state.EnsureOutsideMigrationCallback();
        if (!ReferenceEquals(state.Host, source) || !ReferenceEquals(source.World, World)) {
            throw new InvalidOperationException("Migration source does not own the entity or belongs to another world.");
        }
        if (state.Releasing) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        var sourceEntities = source.UnsafeGetEntitySpan();
        if ((uint)state.Slot < (uint)sourceEntities.Length && sourceEntities[state.Slot] == entity) {
            throw new InvalidOperationException("Cannot move an entity that is still present in its source host.");
        }
        AppendRow(entity, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendRow(Entity entity, in TEntity data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (typeof(TBuffer) == typeof(ArrayBuffer<TEntity>)) {
            var buffer = (ArrayBuffer<TEntity>)(object)Buffer;
            ReserveArrayRow(buffer);
            AppendReservedArrayRow(buffer, entity, entity.GetStateUnchecked(), this, data);
            return;
        }
        AppendRowWithRollback(entity, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReserveArrayRow(ArrayBuffer<TEntity> buffer)
    {
        // Reserve both backing stores before publishing a row or acquiring its identity.
        var count = buffer.Count + 1;
        if (count > _entities.Capacity) {
            _entities.EnsureCapacity(count);
        }

        buffer.EnsureCapacity(count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendReservedArrayRow(
        ArrayBuffer<TEntity> buffer, Entity entity, EntityState state, IEntityHost owner, in TEntity data)
    {
        var slot = buffer.Count;
        buffer.AppendReserved(data);
        _entities.Add(entity);
        state.Host = owner;
        state.Slot = slot;
        Version++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AppendRowWithRollback(Entity entity, in TEntity data)
    {
        var value = data;
        var slot = Buffer.Count;
        var count = checked(slot + 1);
        _entities.EnsureCapacity(count);
        try {
            Buffer.Count = count;
            Buffer.GetRef(slot) = value;
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error).Attempt(() => {
                if (Buffer.Count != slot) {
                    Buffer.Count = slot;
                }
            }).ThrowFailure();
        }
        _entities.Add(entity);
        var state = entity.GetStateUnchecked();
        state.Host = this;
        state.Slot = slot;
        Version++;
    }

    public ref byte GetByteRef(int slot)
        => ref Unsafe.As<TEntity, byte>(ref GetRef(slot));

    public ref TEntity GetRef(int slot)
        => ref Buffer.GetRef(slot);

    public void GetHList<THandler>(int slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => handler.Handle(ref Buffer.GetRef(slot));

    public virtual void Add<TComponent>(Entity entity, in TComponent initial)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset != -1) {
            EntityExceptionHelper.ThrowComponentExisted<TComponent>();
        }
        var state = entity.GetState();
        EnsureOwnSlot(entity, state);
        state.EnsureOutsideMigrationCallback();
        var host = state.Host!;
        var moved = HList.Cons(initial, Buffer.GetRef(state.Slot));
        var siblingHost = ResolveSibling<HList<TComponent, TEntity>>(entity, state, host);
        host.MoveOut(entity, siblingHost);
        siblingHost.MoveIn(entity, moved, host);
    }

    internal void AddDefault<TComponent>(Entity entity, in TComponent initial)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset != -1) {
            EntityExceptionHelper.ThrowComponentExisted<TComponent>();
        }
        var state = entity.GetState();
        EnsureOwnSlot(entity, state);
        state.EnsureOutsideMigrationCallback();
        var host = state.Host!;
        var value = HList.Cons(initial, Buffer.GetRef(state.Slot));
        if (RuntimeFeature.IsDynamicCodeSupported && typeof(TBuffer) == typeof(ArrayBuffer<TEntity>)
            && TryGetArrayHosts<HList<TComponent, TEntity>>(host, out var source, out var target)) {
            MoveBetweenArrayHosts(source, target, entity, state, value);
            return;
        }
        var siblingHost = ResolveSibling<HList<TComponent, TEntity>>(entity, state, host);
        host.MoveOut(entity, siblingHost);
        siblingHost.MoveIn(entity, value, host);
    }

    private static IEntityHost<T> ResolveSibling<T>(Entity entity, EntityState state, IEntityHost host)
        where T : struct, IHList
    {
        state.EnsureOutsideMigrationCallback();
        state.NotifyingMigration = true;
        var world = host.World;
        world?.BeginHostMigration();
        try { return host.GetSiblingHost<T>(); }
        finally {
            if (entity.References(state)) {
                state.NotifyingMigration = false;
            }

            world?.EndHostMigration();
        }
    }

    public virtual bool TryAddAll<TComponent>(ReadOnlySpan<Entity> entities, in TComponent initial) => false;

    public virtual bool TryRemoveAll<TComponent>(ReadOnlySpan<Entity> entities) => false;

    private static bool TryGetArrayHosts<TDestination>(IEntityHost host,
        [NotNullWhen(true)] out WorldEntityHost<TEntity, ArrayEntityHost<TEntity>>? source,
        [NotNullWhen(true)] out WorldEntityHost<TDestination, ArrayEntityHost<TDestination>>? target)
        where TDestination : struct, IHList
    {
        source = host as WorldEntityHost<TEntity, ArrayEntityHost<TEntity>>;
        target = null;
        return source is not null && source.InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)
            && source.World.TryGetHost<TDestination, ArrayEntityHost<TDestination>>(out target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MoveBetweenArrayHosts<TDestination>(
        WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source,
        WorldEntityHost<TDestination, ArrayEntityHost<TDestination>> target,
        Entity entity, EntityState state, in TDestination data)
        where TDestination : struct, IHList
    {
        BufferEntityHost<TDestination, ArrayBuffer<TDestination>> destination = target.InnerHost;
        if (target.InnerHost.GetType() != typeof(ArrayEntityHost<TDestination>)
            || !ReferenceEquals(source.World, target.World)
            || source.HasMigrationObservers || target.HasMigrationObservers) {
            source.MoveOut(entity, target);
            target.MoveIn(entity, data, source);
            return;
        }

        ObjectDisposedException.ThrowIf(target.IsClosedForCreation || destination._disposed, target);
        source.InnerHost.EnsureOwnSlot(entity, state);
        var buffer = destination.Buffer;
        destination.ReserveArrayRow(buffer);
        source.InnerHost.RemoveRow(state);
        destination.AppendReservedArrayRow(buffer, entity, state, target, data);
    }

    private struct EntityMover(Entity e)
        : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T data)
            where T : struct, IHList
        {
            var state = e.GetState();
            var host = state.Host!;
            var moved = data;
            var siblingHost = ResolveSibling<T>(e, state, host);
            host.MoveOut(e, siblingHost);
            siblingHost.MoveIn(e, moved, host);
        }
    }

    private readonly struct UnmanagedEntityMover(Entity e) : IGenericStructHandler<IHList>
    {
        public void Handle<T>(in T data) where T : struct, IHList
        {
            var host = e.GetState().Host!;
            if (host is WorldEntityHost<TEntity, UnmanagedArrayEntityHost<TEntity>> source
                && source.InnerHost.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>)
                && source.World.TryGetHost<T, UnmanagedArrayEntityHost<T>>(out var target)) {
                var moved = data;
                source.MoveOut(e, target);
                target.MoveIn(e, moved, source);
            }
            else {
                new EntityMover(e).Handle(data);
            }
        }
    }

    private readonly struct ArrayEntityMover(Entity e) : IGenericStructHandler<IHList>
    {
        public void Handle<T>(in T data) where T : struct, IHList
        {
            var state = e.GetState();
            var host = state.Host!;
            var moved = data;
            if (TryGetArrayHosts<T>(host, out var source, out var target)) {
                MoveBetweenArrayHosts(source, target, e, state, moved);
            }
            else {
                var siblingHost = ResolveSibling<T>(e, state, host);
                host.MoveOut(e, siblingHost);
                siblingHost.MoveIn(e, moved, host);
            }
        }
    }

    private struct EntityComponentChecker : IGenericTypeHandler
    {
        public static EntityComponentChecker Instance = new();

        public readonly void Handle<T>()
        {
            if (EntityIndexer<TEntity, T>.Offset != -1) {
                EntityExceptionHelper.ThrowComponentExisted<T>();
            }
        }
    }

    public virtual void AddMany<TList>(Entity entity, in TList list)
        where TList : struct, IHList
    {
        var state = entity.GetState();
        EnsureOwnSlot(entity, state);
        state.EnsureOutsideMigrationCallback();
        TList.HandleTypes(EntityComponentChecker.Instance);

        ref var data = ref Buffer.GetRef(state.Slot);
        var mover = new EntityMover(entity);
        data.Concat(list, mover);
    }

    public virtual void Set<TComponent>(Entity entity, in TComponent value)
    {
        var state = entity.GetState();
        EnsureOwnSlot(entity, state);
        var offset = EntityIndexer<TEntity, TComponent>.Offset;
        if (offset == -1) {
            Add(entity, value);
            return;
        }
        ref var data = ref GetRef(state.Slot);
        Unsafe.As<TEntity, TComponent>(
            ref Unsafe.AddByteOffset(ref data, offset))
            = value;
    }

    public virtual void Remove<TComponent>(Entity entity, out bool success)
    {
        var state = entity.GetState();
        EnsureOwnSlot(entity, state);
        if (EntityIndexer<TEntity, TComponent>.Offset == -1) {
            success = false;
            return;
        }
        state.EnsureOutsideMigrationCallback();
        ref var data = ref Buffer.GetRef(state.Slot);
        var mover = new EntityMover(entity);
        data.Remove(TypeProxy<TComponent>._, mover);
        success = true;
    }

    internal void RemoveDefault<TComponent>(Entity entity, out bool success)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || typeof(TBuffer) != typeof(ArrayBuffer<TEntity>)
            || !BulkHListShape.IsStandard) {
            if (EntityIndexer<TEntity, TComponent>.Offset == -1) {
                success = false;
                return;
            }
            ref var fallbackData = ref Buffer.GetRef(entity.GetStateUnchecked().Slot);
            if (RuntimeFeature.IsDynamicCodeSupported && typeof(TBuffer) == typeof(UnmanagedArrayBuffer<TEntity>)
                && BulkHListShape.IsStandard) {
                var unmanagedMover = new UnmanagedEntityMover(entity);
                fallbackData.Remove(TypeProxy<TComponent>._, unmanagedMover);
            }
            else {
                var fallbackMover = new EntityMover(entity);
                fallbackData.Remove(TypeProxy<TComponent>._, fallbackMover);
            }
            success = true;
            return;
        }
        if (EntityIndexer<TEntity, TComponent>.Offset == -1) {
            success = false;
            return;
        }
        ref var data = ref Buffer.GetRef(entity.GetStateUnchecked().Slot);
        var mover = new ArrayEntityMover(entity);
        data.Remove(TypeProxy<TComponent>._, mover);
        success = true;
    }

    private readonly struct EntityComponentPredicate<TList> : IGenericPredicate
        where TList : struct, IHList
    {
        public static EntityComponentPredicate<TList> Instance = new();

        private struct TypeCollector(HashSet<Type> set) : IGenericTypeHandler
        {
            public readonly void Handle<T>()
                => set.Add(typeof(T));
        }

        private readonly HashSet<Type> _types = [];

        public EntityComponentPredicate()
            => TList.HandleTypes(new TypeCollector(_types));

        public readonly bool Predicate<T>(in T value)
            => !_types.Contains(typeof(T));
    }

    private struct FilteredHListMover(Entity e)
        : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : struct, IHList
        {
            if (typeof(T) == typeof(TEntity)) {
                return;
            }
            var state = e.GetState();
            var host = state.Host!;
            var moved = value;
            var siblingHost = ResolveSibling<T>(e, state, host);
            host.MoveOut(e, siblingHost);
            siblingHost.MoveIn(e, moved, host);
        }
    }

    public virtual void RemoveMany<TList>(Entity entity)
        where TList : struct, IHList
    {
        var state = entity.GetState();
        EnsureOwnSlot(entity, state);
        state.EnsureOutsideMigrationCallback();
        ref var data = ref Buffer.GetRef(state.Slot);
        data.Filter(
            EntityComponentPredicate<TList>.Instance,
            new FilteredHListMover(entity));
    }

    public virtual IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : struct, IHList
        => throw new NotSupportedException("Sibling host not supported");

    public virtual void GetSiblingHostType<UEntity, THandler>(in THandler hostTypeHandler)
        where UEntity : struct, IHList
        where THandler : IGenericConcreteTypeHandler<IEntityHost<UEntity>>
        => throw new NotSupportedException("Sibling host not supported");

    public Span<Entity> UnsafeGetEntitySpan()
        => _entities.AsSpan();

    public object Box(int slot)
        => Buffer.GetRef(slot);

    public IEnumerator<Entity> GetEnumerator() => _entities.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        World?.EnsureNoHostMigration();
        _disposed = true;
        foreach (var entity in _entities) {
            var state = entity.GetStateUnchecked();
            if (_ownsEntityStates) {
                state.Host = null;
                state.Slot = -1;
            }
            else {
                EntityStates.Return(state);
            }
        }
        _entities.Clear();
        if (_ownsEntityStates) {
            _entityStates?.Retire();
        }
        Buffer.AsSpan()[..Buffer.Count].Clear();
        Buffer.Count = 0;
        Buffer.Dispose();
        World = null;
        OnDisposed?.Invoke(this);
        GC.SuppressFinalize(this);
    }

}
