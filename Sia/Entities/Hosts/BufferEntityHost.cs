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

    bool IEntityHost.IsClosed => _disposed;

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
        var state = GetEntityState(entity);
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
        var state = GetEntityState(entity);
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
    private EntityState GetEntityState(Entity entity)
    {
        var state = entity.GetState();
        if ((uint)state.Slot >= (uint)_entities.Count || _entities[state.Slot] != entity) {
            throw new InvalidOperationException("Entity belongs to another host.");
        }
        return state;
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
        var state = GetEntityState(entity);
        Move(entity, HList.Cons(initial, Buffer.GetRef(state.Slot)));
    }

    internal void AddDefault<TComponent>(Entity entity, in TComponent initial)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset != -1) {
            EntityExceptionHelper.ThrowComponentExisted<TComponent>();
        }
        var state = entity.GetStateUnchecked();
        ref var data = ref Buffer.GetRef(state.Slot);
        var host = state.Host!;
        if (RuntimeFeature.IsDynamicCodeSupported && typeof(TBuffer) == typeof(ArrayBuffer<TEntity>)
            && TryGetArrayTarget<HList<TComponent, TEntity>>(host, out var source, out var target)) {
            var value = HList.Cons(initial, data);
            source.MoveOut(entity, target);
            target.MoveIn(entity, value, source);
            return;
        }
        Move(entity, HList.Cons(initial, data));
    }

    public virtual bool TryAddAll<TComponent>(ReadOnlySpan<Entity> entities, in TComponent initial) => false;

    public virtual bool TryRemoveAll<TComponent>(ReadOnlySpan<Entity> entities) => false;

    private static bool TryGetArrayTarget<TDestination>(IEntityHost host,
        [NotNullWhen(true)] out WorldEntityHost<TEntity, ArrayEntityHost<TEntity>>? source,
        [NotNullWhen(true)] out WorldEntityHost<TDestination, ArrayEntityHost<TDestination>>? target)
        where TDestination : struct, IHList
    {
        source = host as WorldEntityHost<TEntity, ArrayEntityHost<TEntity>>;
        target = null;
        return source is not null && source.InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)
            && source.World.TryGetHost<TDestination, ArrayEntityHost<TDestination>>(out target);
    }

    private static void Move<T>(Entity entity, T data) where T : struct, IHList
    {
        var state = entity.GetState();
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

    private struct EntityMover(Entity e)
        : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T data)
            where T : struct, IHList
        {
            Move(e, data);
        }
    }

    private readonly struct UnmanagedEntityMover(Entity e) : IGenericStructHandler<IHList>
    {
        public void Handle<T>(in T data) where T : struct, IHList
        {
            var host = e.GetStateUnchecked().Host!;
            if (host is WorldEntityHost<TEntity, UnmanagedArrayEntityHost<TEntity>> source
                && source.InnerHost.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>)
                && source.World.TryGetHost<T, UnmanagedArrayEntityHost<T>>(out var target)) {
                var value = data;
                source.MoveOut(e, target);
                target.MoveIn(e, value, source);
            }
            else Move(e, data);
        }
    }

    private readonly struct ArrayEntityMover(Entity e) : IGenericStructHandler<IHList>
    {
        public void Handle<T>(in T data) where T : struct, IHList
        {
            var host = e.GetStateUnchecked().Host!;
            if (TryGetArrayTarget<T>(host, out var source, out var target)) {
                var value = data;
                source.MoveOut(e, target);
                target.MoveIn(e, value, source);
            }
            else {
                Move(e, data);
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
        TList.HandleTypes(EntityComponentChecker.Instance);

        ref var data = ref Buffer.GetRef(GetEntityState(entity).Slot);
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
        ref var data = ref GetRef(GetEntityState(entity).Slot);
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
        ref var data = ref Buffer.GetRef(GetEntityState(entity).Slot);
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
            Move(e, value);
        }
    }

    public virtual void RemoveMany<TList>(Entity entity)
        where TList : struct, IHList
    {
        ref var data = ref Buffer.GetRef(GetEntityState(entity).Slot);
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
