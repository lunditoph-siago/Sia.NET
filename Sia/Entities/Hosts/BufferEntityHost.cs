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

    public bool IsClosed => _disposed;

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
        var entity = EntityStates.Rent();
        MoveInTrusted(entity, initial);
        return entity;
    }

    public virtual void Release(Entity entity)
    {
        var state = GetEntityState(entity, out _);
        if (state.NotifyingMigration) {
            throw new InvalidOperationException("Cannot release an entity during a migration callback.");
        }
        MoveOut(state);
        EntityStates.Return(state);
    }

    public Entity GetEntity(int slot)
        => _entities[slot];

    public void MoveOut(Entity entity, IEntityHost destination)
    {
        var state = GetEntityState(entity, out _);
        if (state.IsTransitioning) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        if (!ReferenceEquals(destination.World, World)) {
            throw new InvalidOperationException("Migration destination belongs to another world.");
        }
        ObjectDisposedException.ThrowIf(destination.IsClosed, destination);
        MoveOut(state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntityState GetEntityState(in Entity entity, out int slot)
    {
        var state = entity.GetStateUnchecked();
        slot = state?.Slot ?? -1;
        if ((uint)slot >= (uint)_entities.Count || _entities[slot] != entity) {
            entity.GetState();
            throw new InvalidOperationException("Entity belongs to another host.");
        }
        return state!;
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
        if (state.IsTransitioning) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        if (!ReferenceEquals(state.Host, source) || !ReferenceEquals(source.World, World)) {
            throw new InvalidOperationException("Entity has not moved out of a source host in this world.");
        }
        var entities = source.UnsafeGetEntitySpan();
        if ((uint)state.Slot < (uint)entities.Length && entities[state.Slot] == entity) {
            throw new InvalidOperationException("Entity has not moved out of a source host in this world.");
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
        var state = GetEntityState(entity, out var slot);
        if (state.IsTransitioning) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        Move(entity, state, this, HList.Cons(initial, Buffer.GetRef(slot)), useBuiltIn: true);
    }

    internal void AddDefault<TComponent>(Entity entity, EntityState state, in TComponent initial)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset != -1) {
            EntityExceptionHelper.ThrowComponentExisted<TComponent>();
        }
        Move(entity, state, this, HList.Cons(initial, Buffer.GetRef(state.Slot)), useBuiltIn: true);
    }

    public virtual bool TryAddAll<TComponent>(ReadOnlySpan<Entity> entities, in TComponent initial) => false;

    public virtual bool TryRemoveAll<TComponent>(ReadOnlySpan<Entity> entities) => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Move<TDestination, TStorage, TSourceHost, TTargetHost>(
        WorldEntityHost<TEntity, TSourceHost> source, WorldEntityHost<TDestination, TTargetHost> target,
        BufferEntityHost<TDestination, TStorage> destination, Entity entity, in TDestination data)
        where TDestination : struct, IHList
        where TStorage : IBuffer<TDestination>
        where TSourceHost : IEntityHost<TEntity>, new()
        where TTargetHost : IEntityHost<TDestination>, new()
    {
        var state = GetEntityState(in entity, out _);
        if (!ReferenceEquals(source.World, target.World)) {
            throw new InvalidOperationException("Migration destination belongs to another world.");
        }
        ObjectDisposedException.ThrowIf(target.IsDisposed || destination._disposed || source.World.IsClosingHost(target), target);
        if (source.NotifyMigration(entity, state, target, incoming: false)) {
            ObjectDisposedException.ThrowIf(target.IsDisposed || destination._disposed || source.World.IsClosingHost(target), target);
            GetEntityState(in entity, out _);
        }
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
        target.NotifyMigration(entity, state, source, incoming: true);
    }

    private static void Move<T>(Entity entity, T data, BufferEntityHost<TEntity, TBuffer> source)
        where T : struct, IHList
    {
        var state = source.GetEntityState(entity, out _);
        if (state.IsTransitioning) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        var host = state.Host!;
        var world = host.World;
        IEntityHost<T> sibling;
        state.NotifyingMigration = true;
        if (world != null) world.HostCallbackDepth++;
        try {
            sibling = host.GetSiblingHost<T>();
        }
        finally {
            state.NotifyingMigration = false;
            if (world != null) world.HostCallbackDepth--;
        }
        host.MoveOut(entity, sibling);
        sibling.MoveIn(entity, data, host);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Move<T>(Entity entity, EntityState state, BufferEntityHost<TEntity, TBuffer> source,
        in T data, bool useBuiltIn)
        where T : struct, IHList
    {
        var host = state.Host!;
        var moved = data;
        if (useBuiltIn && typeof(T) != typeof(TEntity)
            && RuntimeFeature.IsDynamicCodeSupported && typeof(TBuffer) == typeof(ArrayBuffer<TEntity>)
            && host is WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> managed
            && managed.InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)
            && managed.World.TryGetHost<T, ArrayEntityHost<T>>(out var managedTarget)
            && managedTarget.InnerHost.GetType() == typeof(ArrayEntityHost<T>)) {
            source.Move(managed, managedTarget, managedTarget.InnerHost, entity, moved);
        }
        else if (useBuiltIn && typeof(T) != typeof(TEntity)
            && RuntimeFeature.IsDynamicCodeSupported && typeof(TBuffer) == typeof(UnmanagedArrayBuffer<TEntity>)
            && host is WorldEntityHost<TEntity, UnmanagedArrayEntityHost<TEntity>> unmanaged
            && unmanaged.InnerHost.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>)
            && unmanaged.World.TryGetHost<T, UnmanagedArrayEntityHost<T>>(out var unmanagedTarget)
            && unmanagedTarget.InnerHost.GetType() == typeof(UnmanagedArrayEntityHost<T>)) {
            source.Move(unmanaged, unmanagedTarget, unmanagedTarget.InnerHost, entity, moved);
        }
        else Move(entity, moved, source);
    }

    private struct EntityMover(Entity e, BufferEntityHost<TEntity, TBuffer> source)
        : IGenericStructHandler<IHList>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public readonly void Handle<T>(in T data)
            where T : struct, IHList
        {
            var state = e.GetStateUnchecked();
            if (state.IsTransitioning) {
                throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
            }
            Move(e, state, source, data, BulkHListShape.IsStandard);
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
        TList.HandleTypes(EntityComponentChecker.Instance);

        GetEntityState(entity, out var slot);
        ref var data = ref Buffer.GetRef(slot);
        var mover = new EntityMover(entity, this);
        data.Concat(list, mover);
    }

    public virtual void Set<TComponent>(Entity entity, in TComponent value)
    {
        var offset = EntityIndexer<TEntity, TComponent>.Offset;
        if (offset == -1) {
            Add(entity, value);
            return;
        }
        GetEntityState(in entity, out var slot);
        var buffer = Buffer;
        ref var data = ref (buffer is ArrayBuffer<TEntity> array
            ? ref array.GetRef(slot)
            : ref (buffer is UnmanagedArrayBuffer<TEntity> native
                ? ref native.GetRef(slot) : ref buffer.GetRef(slot)));
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
        GetEntityState(entity, out var slot);
        ref var data = ref Buffer.GetRef(slot);
        var mover = new EntityMover(entity, this);
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
        var mover = new EntityMover(entity, this);
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

    private struct FilteredHListMover(Entity e, BufferEntityHost<TEntity, TBuffer> source)
        : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : struct, IHList
        {
            if (typeof(T) == typeof(TEntity)) {
                return;
            }
            new EntityMover(e, source).Handle(value);
        }
    }

    public virtual void RemoveMany<TList>(Entity entity)
        where TList : struct, IHList
    {
        GetEntityState(entity, out var slot);
        ref var data = ref Buffer.GetRef(slot);
        data.Filter(
            EntityComponentPredicate<TList>.Instance,
            new FilteredHListMover(entity, this));
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
        if (Count != 0 && World is { } world && (world.HostCallbackDepth != 0 || world.Dispatcher.IsSending)) {
            throw new InvalidOperationException("Cannot remove a host during entity callbacks or event dispatch.");
        }
        if (_disposed) return;
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
