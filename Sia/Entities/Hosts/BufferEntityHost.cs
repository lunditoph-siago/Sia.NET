namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;

public static class BufferEntityHost<TEntity>
    where TEntity : struct, IHList
{
    public static BufferEntityHost<TEntity, TBuffer> Create<TBuffer>(TBuffer buffer)
        where TBuffer : IBuffer<TEntity>
        => new(buffer);
}

public class BufferEntityHost<TEntity, TBuffer>(TBuffer buffer)
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
        MoveOut(state);
        EntityStates.Return(state);
    }

    public Entity GetEntity(int slot)
        => _entities[slot];

    public void MoveOut(Entity entity, IEntityHost destination)
        => MoveOut(entity.GetState());

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
        _ = entity.GetCurrentState();
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
        var state = entity.GetStateUnchecked();
        ref var data = ref Buffer.GetRef(state.Slot);
        var host = state.Host!;
        var siblingHost = host.GetSiblingHost<HList<TComponent, TEntity>>();
        var moved = HList.Cons(initial, data);
        host.MoveOut(entity, siblingHost);
        siblingHost.MoveIn(entity, moved, host);
    }

    private struct EntityMover(Entity e)
        : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T data)
            where T : struct, IHList
        {
            var host = e.GetStateUnchecked().Host!;
            var siblingHost = host.GetSiblingHost<T>();
            host.MoveOut(e, siblingHost);
            siblingHost.MoveIn(e, data, host);
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
            var host = e.GetStateUnchecked().Host!;
            var siblingHost = host.GetSiblingHost<T>();
            host.MoveOut(e, siblingHost);
            siblingHost.MoveIn(e, value, host);
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

    internal bool TryBulkAddAll<TComponent>(
        WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source,
        WorldEntityHost<HList<TComponent, TEntity>, ArrayEntityHost<HList<TComponent, TEntity>>> target,
        ReadOnlySpan<Entity> expectedEntities, ReadOnlySpan<TComponent> initialValues)
    {
        var world = source.World;
        world.EnsureOwnerThread();
        var destination = target.InnerHost;
        if (!ReferenceEquals(this, source.InnerHost) || !ReferenceEquals(world, target.World)
            || GetType() != typeof(ArrayEntityHost<TEntity>)
            || destination.GetType() != typeof(ArrayEntityHost<HList<TComponent, TEntity>>)
            || !ReferenceEquals(World, world) || !ReferenceEquals(destination.World, world)
            || !BulkHListShape.IsStandard
            || source.HasMigrationObservers || target.HasMigrationObservers || !world.Dispatcher.HasNoUntypedListeners
            || Buffer is not ArrayBuffer<TEntity> sourceBuffer
            || EntityIndexer<TEntity, TComponent>.Offset != -1
            || _entities.Count != sourceBuffer.Count || destination._entities.Count != destination.Buffer.Count) return false;
        var count = _entities.Count;
        if (initialValues.Length != count || expectedEntities.Length != count) return false;
        if (count == 0) return true;
        if (!world.Dispatcher.HasNoTypedListeners<WorldEvents.Add<TComponent>>()
            || !world.Dispatcher.HasNoTypedListeners<WorldEvents.Set<TComponent>>()) return false;
        for (var i = 0; i < count; i++) {
            var e = _entities[i];
            if (e != expectedEntities[i] || !e.IsValid) return false;
            var state = e.GetStateUnchecked();
            if (!ReferenceEquals(state.Host, source) || state.Slot != i || state.Releasing || state.EventSink is not null) return false;
        }
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
        for (var i = 0; i < count; i++) {
            var e = _entities[i];
            targetRows[start + i] = HList.Cons(initialValues[i], sourceRows[i]);
            var state = e.GetStateUnchecked();
            state.Host = target;
            state.Slot = start + i;
        }
        destination._entities.AddRange(_entities);
        Version += count;
        destination.Version += count;
        sourceRows[..count].Clear();
        _entities.Clear();
        sourceBuffer.Count = 0;
        return true;
    }

    internal bool TryBulkRemoveHeadAll<THead, TTail>(
        WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> source,
        WorldEntityHost<TTail, ArrayEntityHost<TTail>> target, ReadOnlySpan<Entity> expectedEntities)
        where TTail : struct, IHList
    {
        var world = source.World;
        world.EnsureOwnerThread();
        var destination = target.InnerHost;
        if (typeof(TEntity) != typeof(HList<THead, TTail>)
            || !ReferenceEquals(this, source.InnerHost) || !ReferenceEquals(world, target.World)
            || GetType() != typeof(ArrayEntityHost<TEntity>) || destination.GetType() != typeof(ArrayEntityHost<TTail>)
            || !ReferenceEquals(World, world) || !ReferenceEquals(destination.World, world)
            || source.HasMigrationObservers || target.HasMigrationObservers || !world.Dispatcher.HasNoUntypedListeners
            || Buffer is not ArrayBuffer<TEntity> sourceBuffer
            || _entities.Count != sourceBuffer.Count || destination._entities.Count != destination.Buffer.Count) return false;
        var count = _entities.Count;
        if (expectedEntities.Length != count) return false;
        if (count == 0) return true;
        if (!world.Dispatcher.HasNoTypedListeners<WorldEvents.Remove<THead>>()) return false;
        for (var i = 0; i < count; i++) {
            var e = _entities[i];
            if (e != expectedEntities[i] || !e.IsValid) return false;
            var state = e.GetStateUnchecked();
            if (!ReferenceEquals(state.Host, source) || state.Slot != i || state.Releasing || state.EventSink is not null) return false;
        }
        _ = WorldEvents.Remove<THead>.Instance;
        world.EnsureOwnerThread();
        _ = EventTypeIndexer<WorldEvents.Remove<THead>>.Index;
        var start = destination.Buffer.Count;
        var end = checked(start + count);
        destination._entities.EnsureCapacity(end);
        destination.Buffer.Count = end;
        var sourceRows = sourceBuffer.AsSpan();
        var targetRows = destination.Buffer.AsSpan();
        for (var i = 0; i < count; i++) {
            var e = _entities[i];
            targetRows[start + i] = Unsafe.As<TEntity, HList<THead, TTail>>(ref sourceRows[i]).Tail;
            var state = e.GetStateUnchecked();
            state.Host = target;
            state.Slot = start + i;
        }
        destination._entities.AddRange(_entities);
        Version += count;
        destination.Version += count;
        sourceRows[..count].Clear();
        _entities.Clear();
        sourceBuffer.Count = 0;
        return true;
    }
}
