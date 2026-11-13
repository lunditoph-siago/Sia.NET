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

    private EntityStatePool EntityStates {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _entityStates ??= new();
    }

    void IEntityHost.OnInitialize(World world)
    {
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
        var e = EntityStates.Rent();
        MoveInTrusted(e, initial);
        return e;
    }

    public virtual void Release(Entity entity)
    {
        var state = entity.GetState();
        state.EnsureOutsideMigrationCallback();
        if (state.Migrating) {
            throw new InvalidOperationException("Cannot release an entity while it is being migrated.");
        }
        EnsureOwnSlot(entity, state);
        MoveOut(state);
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
        MoveOut(state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureOwnSlot(Entity entity, EntityState state)
    {
        if ((uint)state.Slot >= (uint)_entities.Count || _entities[state.Slot] != entity) {
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
        var state = entity.GetCurrentState();
        state.EnsureOutsideMigrationCallback();
        if (!ReferenceEquals(state.Host, source) || !ReferenceEquals(source.World, World)) {
            throw new InvalidOperationException("Migration source does not own the entity or belongs to another world.");
        }
        if (state.Releasing) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        MoveInTrusted(entity, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MoveInTrusted(Entity entity, in TEntity data)
    {
        Version++;

        var slot = Buffer.Count++;
        Buffer.GetRef(slot) = data;

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
        var state = entity.GetState();
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
        state.EnsureOutsideMigrationCallback();
        var host = state.Host!;
        var value = HList.Cons(initial, Buffer.GetRef(state.Slot));
        if (RuntimeFeature.IsDynamicCodeSupported && typeof(TBuffer) == typeof(ArrayBuffer<TEntity>)
            && TryGetArrayTarget<HList<TComponent, TEntity>>(host, out var source, out var target)) {
            source.MoveOut(entity, target);
            target.MoveIn(entity, value, source);
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
            if (entity.References(state)) state.NotifyingMigration = false;
            world?.EndHostMigration();
        }
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
            else new EntityMover(e).Handle(data);
        }
    }

    private readonly struct ArrayEntityMover(Entity e) : IGenericStructHandler<IHList>
    {
        public void Handle<T>(in T data) where T : struct, IHList
        {
            var state = e.GetState();
            var host = state.Host!;
            var moved = data;
            if (TryGetArrayTarget<T>(host, out var source, out var target)) {
                source.MoveOut(e, target);
                target.MoveIn(e, moved, source);
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
        TList.HandleTypes(EntityComponentChecker.Instance);

        ref var data = ref Buffer.GetRef(entity.GetStateUnchecked().Slot);
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
        ref var data = ref GetRef(entity.GetStateUnchecked().Slot);
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
        ref var data = ref Buffer.GetRef(entity.GetStateUnchecked().Slot);
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
        ref var data = ref Buffer.GetRef(entity.GetStateUnchecked().Slot);
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
        World?.EnsureNoHostMigration();
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
