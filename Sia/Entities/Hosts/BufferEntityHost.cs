using System.Collections;
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
        if (Buffer.Count == 0
            || RuntimeHelpers.IsReferenceOrContainsReferences<TEntity>()
            || (long)Buffer.Count * Unsafe.SizeOf<TEntity>() > int.MaxValue) {
            bytes = default;
            return false;
        }
        bytes = MemoryMarshal.Cast<TEntity, byte>(Buffer.AsSpan());
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
        var e = EntityStates.Rent();
        MoveInTrusted(e, initial);
        return e;
    }

    public virtual void Release(Entity entity)
    {
        var state = GetOwnedState(entity);
        if (state.NotifyingMigration) state.EnsureCanMigrate();
        MoveOut(state);
        EntityStates.Return(state);
    }

    public Entity GetEntity(int slot)
        => _entities[slot];

    public void MoveOut(Entity entity, IEntityHost destination)
    {
        var state = GetOwnedState(entity);
        state.EnsureCanMigrate();
        if (!ReferenceEquals(destination.World, World)) {
            throw new InvalidOperationException("Migration destination belongs to another world.");
        }
        destination.EnsureCanMoveIn();
        MoveOut(state);
    }

    void IEntityHost.EnsureCanMoveIn() => ObjectDisposedException.ThrowIf(_disposed, this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntityState GetOwnedState(Entity entity)
    {
        var state = entity.GetState();
        EnsureOwnedSlot(entity, state.Slot);
        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureOwnedSlot(Entity entity, int slot)
    {
        var entities = _entities.AsSpan();
        if ((uint)slot >= (uint)entities.Length || entities[slot] != entity) {
            entity.GetState();
            throw new InvalidOperationException("Entity belongs to another host.");
        }
    }

    private void MoveOut(EntityState state)
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        var state = entity.GetCurrentState();
        state.EnsureCanMigrate();
        if (!ReferenceEquals(state.Host, source) || !ReferenceEquals(source.World, World)) {
            throw new InvalidOperationException("Migration source does not own the entity or belongs to another world.");
        }
        var entities = source.UnsafeGetEntitySpan();
        if ((uint)state.Slot < (uint)entities.Length && entities[state.Slot] == entity) {
            throw new InvalidOperationException("Cannot move an entity still present in its source host.");
        }
        MoveInTrusted(entity, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MoveInTrusted(Entity entity, in TEntity data)
    {
        Version++;

        var value = data;
        var slot = Buffer.Count++;
        Buffer.GetRef(slot) = value;

        var state = entity.GetStateUnchecked();
        state.Host = this;
        state.Slot = slot;
        _entities.Add(entity);
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
        var state = GetOwnedState(entity);
        state.EnsureCanMigrate();
        var host = state.Host!;
        var moved = HList.Cons(initial, Buffer.GetRef(state.Slot));
        var siblingHost = ResolveSibling<HList<TComponent, TEntity>>(entity, state, host);
        host.MoveOut(entity, siblingHost);
        siblingHost.MoveIn(entity, moved, host);
    }

    internal void AddDefault<TComponent>(Entity entity, EntityState state, in TComponent initial)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset != -1) {
            EntityExceptionHelper.ThrowComponentExisted<TComponent>();
        }
        var moved = HList.Cons(initial, Buffer.GetRef(state.Slot));
        if (TryMoveCached(entity, state, moved)) return;

        var host = state.Host!;
        var siblingHost = ResolveSibling<HList<TComponent, TEntity>>(entity, state, host);
        host.MoveOut(entity, siblingHost);
        siblingHost.MoveIn(entity, moved, host);
    }

    public virtual bool TryAddAll<TComponent>(ReadOnlySpan<Entity> entities, in TComponent initial) => false;

    public virtual bool TryRemoveAll<TComponent>(ReadOnlySpan<Entity> entities) => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryMoveCached<TDestination>(Entity entity, EntityState state, in TDestination data)
        where TDestination : struct, IHList
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || typeof(TDestination) == typeof(TEntity)) return false;
        if (typeof(TBuffer) == typeof(ArrayBuffer<TEntity>)
            && state.Host is WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> managed
            && managed.InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)
            && managed.World.TryGetHost<TDestination, ArrayEntityHost<TDestination>>(out var managedTarget)
            && managedTarget.InnerHost.GetType() == typeof(ArrayEntityHost<TDestination>)) {
            managed.InnerHost.MoveCached(managed, managedTarget, managedTarget.InnerHost, entity, state, data);
            return true;
        }
        if (typeof(TBuffer) == typeof(UnmanagedArrayBuffer<TEntity>)
            && state.Host is WorldEntityHost<TEntity, UnmanagedArrayEntityHost<TEntity>> unmanaged
            && unmanaged.InnerHost.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>)
            && unmanaged.World.TryGetHost<TDestination, UnmanagedArrayEntityHost<TDestination>>(out var unmanagedTarget)
            && unmanagedTarget.InnerHost.GetType() == typeof(UnmanagedArrayEntityHost<TDestination>)) {
            unmanaged.InnerHost.MoveCached(unmanaged, unmanagedTarget, unmanagedTarget.InnerHost, entity, state, data);
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MoveCached<TDestination, TStorage, TSourceHost, TTargetHost>(
        WorldEntityHost<TEntity, TSourceHost> source, WorldEntityHost<TDestination, TTargetHost> target,
        BufferEntityHost<TDestination, TStorage> destination, Entity entity, EntityState state, in TDestination data)
        where TDestination : struct, IHList
        where TStorage : IBuffer<TDestination>
        where TSourceHost : IEntityHost<TEntity>, new()
        where TTargetHost : IEntityHost<TDestination>, new()
    {
        state.EnsureCanMigrate();
        if (!ReferenceEquals(target.World, source.World)) {
            throw new InvalidOperationException("Migration destination belongs to another world.");
        }
        ObjectDisposedException.ThrowIf(target.IsClosedForCreation || destination._disposed, target);
        if (typeof(TStorage) == typeof(UnmanagedArrayBuffer<TDestination>)
            && RuntimeHelpers.IsReferenceOrContainsReferences<TDestination>()) {
            throw new NotSupportedException("Unmanaged storage requires elements without managed references.");
        }
        source.NotifyMovedOut(entity, state, target);
        EnsureOwnedSlot(entity, state.Slot);
        var slot = destination.Buffer.Count;
        var count = checked(slot + 1);
        if (count > destination._entities.Capacity) destination._entities.EnsureCapacity(count);
        destination.Buffer.Count = count;
        destination.Buffer.GetRef(slot) = data;
        MoveOut(state);
        destination._entities.Add(entity);
        state.Host = target;
        state.Slot = slot;
        destination.Version++;
        target.NotifyMovedIn(entity, state, source);
    }

    private static IEntityHost<T> ResolveSibling<T>(Entity entity, EntityState state, IEntityHost host)
        where T : struct, IHList
    {
        state.EnsureCanMigrate();
        var world = host.World;
        state.NotifyingMigration = true;
        world?.BeginHostMigration();
        try {
            return host.GetSiblingHost<T>();
        }
        finally {
            if (entity.References(state)) state.NotifyingMigration = false;
            world?.EndHostMigration();
        }
    }

    private struct EntityMover(Entity e)
        : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T data)
            where T : struct, IHList
        {
            var state = e.GetState();
            var moved = data;
            if (BulkHListShape.IsStandard && TryMoveCached(e, state, moved)) return;

            var host = state.Host!;
            var siblingHost = ResolveSibling<T>(e, state, host);
            host.MoveOut(e, siblingHost);
            siblingHost.MoveIn(e, moved, host);
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
        var state = GetOwnedState(entity);
        state.EnsureCanMigrate();
        TList.HandleTypes(EntityComponentChecker.Instance);

        ref var data = ref Buffer.GetRef(state.Slot);
        var mover = new EntityMover(entity);
        data.Concat(list, mover);
    }

    public virtual void Set<TComponent>(Entity entity, in TComponent value)
    {
        var offset = EntityIndexer<TEntity, TComponent>.Offset;
        if (offset == -1) {
            Add(entity, value);
            return;
        }
        var slot = entity.GetStateUnchecked()?.Slot ?? -1;
        EnsureOwnedSlot(entity, slot);
        var buffer = Buffer;
        ref var data = ref (buffer is ArrayBuffer<TEntity> array
            ? ref array.GetRef(slot) : ref buffer.GetRef(slot));
        Unsafe.As<TEntity, TComponent>(
            ref Unsafe.AddByteOffset(ref data, offset))
            = value;
    }

    public virtual void Remove<TComponent>(Entity entity, out bool success)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset == -1) {
            success = false;
            return;
        }
        var state = GetOwnedState(entity);
        state.EnsureCanMigrate();
        ref var data = ref Buffer.GetRef(state.Slot);
        var mover = new EntityMover(entity);
        data.Remove(TypeProxy<TComponent>._, mover);
        success = true;
    }

    internal void RemoveDefault<TComponent>(Entity entity, EntityState state, out bool success)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset == -1) {
            success = false;
            return;
        }
        ref var data = ref Buffer.GetRef(state.Slot);
        var mover = new EntityMover(entity);
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
            if (typeof(T) == typeof(TEntity)) return;
            new EntityMover(e).Handle(value);
        }
    }

    public virtual void RemoveMany<TList>(Entity entity)
        where TList : struct, IHList
    {
        var state = GetOwnedState(entity);
        if (state.Releasing || state.NotifyingMigration) {
            foreach (var component in EntityDescriptor<TList>.Components) {
                if (Descriptor.Offsets.ContainsKey(component.Type)) state.EnsureCanMigrate();
            }
            return;
        }
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
        if (_disposed) return;
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
