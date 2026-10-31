#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

using System.Collections;
using System.Runtime.CompilerServices;

using static Sia.WorldHostUtils;

namespace Sia;

internal interface IWorldBatchHost
{
    bool IsExactArrayHost { get; }
    bool HasMigrationObservers { get; }
}

public sealed class WorldEntityHost<TEntity, TInnerHost>(World world, TInnerHost innerHost)
    : IEntityHost<TEntity>, IReactiveEntityHost, IWorldBatchHost
    where TEntity : struct, IHList
    where TInnerHost : IEntityHost<TEntity>, new()
{
    private readonly unsafe struct SiblingInnerHostGetter<UEntity>(World world, IEntityHost<UEntity>* host)
        : IGenericConcreteTypeHandler<IEntityHost<UEntity>>
        where UEntity : struct, IHList
    {
        public void Handle<UInnerHost>()
            where UInnerHost : IEntityHost<UEntity>, new()
            => *host = world.TryGetHost<WorldEntityHost<UEntity, UInnerHost>>(out var found)
                ? found : world.UnsafeAddRawHost(new WorldEntityHost<UEntity, UInnerHost>(world, new()));
    }

    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;
    private EntityMigrationHandler? _onEntityMovedOut;
    private EntityMigrationHandler? _onEntityMovedIn;

    public event EntityMigrationHandler? OnEntityMovedOut {
        add => World.UpdateBatchObservers(ref _onEntityMovedOut, value, add: true);
        remove => World.UpdateBatchObservers(ref _onEntityMovedOut, value, add: false);
    }
    public event EntityMigrationHandler? OnEntityMovedIn {
        add => World.UpdateBatchObservers(ref _onEntityMovedIn, value, add: true);
        remove => World.UpdateBatchObservers(ref _onEntityMovedIn, value, add: false);
    }
    public event Action<IEntityHost>? OnDisposed;

    internal bool HasMigrationObservers
        => Volatile.Read(ref _onEntityMovedOut) is not null || Volatile.Read(ref _onEntityMovedIn) is not null;

    bool IWorldBatchHost.IsExactArrayHost => typeof(TInnerHost) == typeof(ArrayEntityHost<TEntity>)
        && InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>);
    bool IWorldBatchHost.HasMigrationObservers => HasMigrationObservers;

    public World World { get; } = world;
    public TInnerHost InnerHost { get; } = InitializeInnerHost(world, innerHost);

    public Type EntityType => InnerHost.EntityType;
    public EntityDescriptor Descriptor => InnerHost.Descriptor;

    public int Capacity => InnerHost.Capacity;
    public int Count => InnerHost.Count;
    public int Version => InnerHost.Version;

    public bool TryGetSequentialBytes(out Span<byte> bytes)
        => InnerHost.TryGetSequentialBytes(out bytes);

    public WorldEntityHost(World world) : this(world, new()) {}

    private static TInnerHost InitializeInnerHost(World world, TInnerHost innerHost)
    {
        innerHost.OnInitialize(world);
        return innerHost;
    }

    public unsafe IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : struct, IHList
    {
        if (typeof(TInnerHost) == typeof(ArrayEntityHost<TEntity>)
            && InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)) {
            return World.TryGetHost<UEntity, ArrayEntityHost<UEntity>>(out var found)
                ? found : World.UnsafeAddRawHost(new WorldEntityHost<UEntity, ArrayEntityHost<UEntity>>(World, new()));
        }
        if (typeof(TInnerHost) == typeof(UnmanagedArrayEntityHost<TEntity>)
            && InnerHost.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>)) {
            return World.TryGetHost<UEntity, UnmanagedArrayEntityHost<UEntity>>(out var found)
                ? found : World.UnsafeAddRawHost(new WorldEntityHost<UEntity, UnmanagedArrayEntityHost<UEntity>>(World, new()));
        }
        IEntityHost<UEntity>? host = null;
        InnerHost.GetSiblingHostType<UEntity, SiblingInnerHostGetter<UEntity>>(
            new(World, &host));
        return host!;
    }

    public void GetSiblingHostType<UEntity, THandler>(in THandler hostTypeHandler)
        where UEntity : struct, IHList
        where THandler : IGenericConcreteTypeHandler<IEntityHost<UEntity>>
        => throw new NotSupportedException("Cannot get concrete sibling type for world hosts");

    public IEnumerator<Entity> GetEnumerator() => InnerHost.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => InnerHost.GetEnumerator();

    public void Dispose()
    {
        InnerHost.Dispose();
        OnDisposed?.Invoke(this);
    }

    public Entity Create() => Create(default!);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Create(in TEntity initial) => Create(initial, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Create(in TEntity initial, out Entity acquired)
    {
        World.EnsureOwnerThread();
        var entity = InnerHost.Create(initial);
        acquired = entity;
        entity.GetStateUnchecked().Host = this;
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        if (!entity.IsValid) return entity;
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        if (entity.IsValid) {
            TEntity.HandleTypes(new EntityAddEventSender(entity, World.Dispatcher));
        }
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(Entity entity) => Release(entity, owned: false);

    public void ReleaseOwned(Entity entity) => Release(entity, owned: true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Release(Entity entity, bool owned)
    {
        World.EnsureOwnerThread();
        var state = entity.GetState();
        if (!ReferenceEquals(state.Host, this)) {
            throw new InvalidOperationException(owned ? "Owned output changed its host." : "Entity belongs to another host.");
        }
        if (state.Releasing) {
            return;
        }
        if (World.Dispatcher.IsSending) {
            DeferRelease(entity, owned);
            state.Releasing = true;
            return;
        }
        state.Releasing = true;
        if (owned) ReleaseOwned(entity, state);
        else Release(entity, state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DeferRelease(Entity entity, bool owned)
        => World.Dispatcher.RunAfterSend(() => {
            if (!entity.IsValid) return;
            var state = entity.GetStateUnchecked();
            if (!ReferenceEquals(state.Host, this)) {
                state.Releasing = false;
                throw new InvalidOperationException("Entity changed its host while release was pending.");
            }
            if (owned) ReleaseOwned(entity, state);
            else Release(entity, state);
        });

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Release(Entity entity, EntityState state)
    {
        try {
            var dispatcher = World.Dispatcher;
            TEntity.HandleTypes(new EntityRemoveEventSender(entity, dispatcher));
            dispatcher.Send(entity, WorldEvents.Remove.Instance);
            dispatcher.UnlistenAll(entity);

            World.Count--;
            try {
                OnEntityReleased?.Invoke(entity);
            }
            catch {
                World.Count++;
                throw;
            }
            InnerHost.Release(entity);
        }
        finally {
            if (entity.References(state)) state.Releasing = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TEntity GetOwnedRef(Entity entity)
    {
        World.EnsureOwnerThread();
        var state = entity.GetState();
        if (!ReferenceEquals(state.Host, this)) {
            throw new InvalidOperationException("Owned output changed its host.");
        }
        return ref InnerHost.GetRef(state.Slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NotifyOwnedSet<T>(Entity entity)
    {
        World.EnsureOwnerThread();
        if (!ReferenceEquals(entity.Host, this)) {
            throw new InvalidOperationException("Owned output changed its host.");
        }
        World.Dispatcher.Send(entity, WorldEvents.Set<T>.Instance);
    }

    private void ReleaseOwned(Entity entity, EntityState state)
    {
        try {
            var dispatcher = World.Dispatcher;
            var result = NotifyOwnedRemoval(entity)
                .Attempt(() => dispatcher.Send(entity, WorldEvents.Remove.Instance))
                .Attempt(() => dispatcher.UnlistenAll(entity));

            World.Count--;
            result
                .Attempt(() => OnEntityReleased?.Invoke(entity))
                .Attempt(() => InnerHost.Release(entity))
                .ThrowIfFailed();
        }
        finally {
            if (entity.References(state)) state.Releasing = false;
        }
    }

    private Outcome<Exception> NotifyOwnedRemoval(Entity entity)
    {
        var sender = new OwnedEntityRemoveEventSender(entity, World.Dispatcher);
        var traversal = Outcome<Exception>.Success.Attempt(() => TEntity.HandleTypes(sender));
        return sender.Result.Combine(traversal);
    }

    private sealed class OwnedEntityRemoveEventSender(Entity entity, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public Outcome<Exception> Result { get; private set; }

        public void Handle<T>()
            => Result = Result.Attempt(() => dispatcher.Send(entity, WorldEvents.Remove<T>.Instance));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add<TComponent>(Entity entity, in TComponent initial)
    {
        World.EnsureOwnerThread();
        if (entity.GetState().Releasing) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        if (typeof(TInnerHost) == typeof(ArrayEntityHost<TEntity>)
            && InnerHost is ArrayEntityHost<TEntity> managed && managed.GetType() == typeof(ArrayEntityHost<TEntity>))
            managed.AddDefault(entity, initial);
        else if (typeof(TInnerHost) == typeof(UnmanagedArrayEntityHost<TEntity>)
            && InnerHost is UnmanagedArrayEntityHost<TEntity> unmanaged && unmanaged.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>))
            unmanaged.AddDefault(entity, initial);
        else InnerHost.Add(entity, initial);
        World.Dispatcher.SendAddAndSet<TComponent>(entity);
    }

    public bool TryAddAll<TComponent>(ReadOnlySpan<Entity> entities, in TComponent initial)
    {
        if (((IWorldBatchHost)this).IsExactArrayHost) return EntityBatch.TryAdd(this, entities, initial);
        if (entities.Length == 1 && InnerHost is BufferEntityHost<TEntity, ArrayBuffer<TEntity>> small
            && InnerHost.GetType() == typeof(TInnerHost) && small.UsesDefaultBatchAdd<TInnerHost>()) return false;
        if (!World.Dispatcher.HasNoTypedListeners<WorldEvents.Add<TComponent>>()
            || !World.Dispatcher.HasNoTypedListeners<WorldEvents.Set<TComponent>>()
            || !CanTryBatch(entities)) return false;
        if (InnerHost is BufferEntityHost<TEntity, ArrayBuffer<TEntity>> buffer
            && buffer.TryAutoBatchAdd(this, entities, initial)) return true;
        return ((IEntityHost)InnerHost).TryAddAll(entities, initial);
    }

    public bool TryRemoveAll<TComponent>(ReadOnlySpan<Entity> entities)
    {
        if (((IWorldBatchHost)this).IsExactArrayHost) return EntityBatch.TryRemove<TComponent>(this, entities);
        if (entities.Length == 1 && InnerHost is BufferEntityHost<TEntity, ArrayBuffer<TEntity>> small
            && InnerHost.GetType() == typeof(TInnerHost) && small.UsesDefaultBatchRemove<TInnerHost>()) return false;
        if (!World.Dispatcher.HasNoTypedListeners<WorldEvents.Remove<TComponent>>() || !CanTryBatch(entities)) return false;
        if (InnerHost is BufferEntityHost<TEntity, ArrayBuffer<TEntity>> buffer
            && buffer.TryAutoBatchRemove<TComponent, TInnerHost>(this, entities)) return true;
        return ((IEntityHost)InnerHost).TryRemoveAll<TComponent>(entities);
    }

    private bool CanTryBatch(ReadOnlySpan<Entity> entities)
    {
        if (entities.IsEmpty) return false;
        World.EnsureOwnerThread();
        if (!World.Dispatcher.HasNoUntypedListeners) return false;
        if (!World.HasNoBatchObservers()) return false;
        foreach (var entity in entities) {
            var state = entity.GetStateUnchecked();
            if (state is null || !entity.References(state) || !ReferenceEquals(state.Host, this)
                || state.Releasing || state.EventSink is not null) return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddMany<TList>(Entity entity, in TList list)
        where TList : struct, IHList
    {
        World.EnsureOwnerThread();
        if (entity.GetState().Releasing) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        InnerHost.AddMany(entity, list);
        TList.HandleTypes(new EntityAddEventSender(entity, World.Dispatcher));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<TComponent>(Entity entity, in TComponent value)
    {
        World.EnsureOwnerThread();
        var state = entity.GetState();
        if (state.Releasing && !Descriptor.Contains<TComponent>()) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        InnerHost.Set(entity, value);
        World.Dispatcher.Send(entity, WorldEvents.Set<TComponent>.Instance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove<TComponent>(Entity entity, out bool success)
    {
        World.EnsureOwnerThread();
        if (entity.GetState().Releasing && Descriptor.Contains<TComponent>()) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        if (typeof(TInnerHost) == typeof(ArrayEntityHost<TEntity>)
            && InnerHost is ArrayEntityHost<TEntity> managed && managed.GetType() == typeof(ArrayEntityHost<TEntity>))
            managed.RemoveDefault<TComponent>(entity, out success);
        else if (typeof(TInnerHost) == typeof(UnmanagedArrayEntityHost<TEntity>)
            && InnerHost is UnmanagedArrayEntityHost<TEntity> unmanaged && unmanaged.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>))
            unmanaged.RemoveDefault<TComponent>(entity, out success);
        else InnerHost.Remove<TComponent>(entity, out success);
        if (success) {
            World.Dispatcher.Send(entity, WorldEvents.Remove<TComponent>.Instance);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveMany<TList>(Entity entity)
        where TList : struct, IHList
    {
        World.EnsureOwnerThread();
        if (entity.GetState().Releasing) {
            foreach (var component in EntityDescriptor<TList>.Components) {
                if (Descriptor.Offsets.ContainsKey(component.Type)) {
                    throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
                }
            }
            return;
        }
        InnerHost.RemoveMany<TList>(entity);
        TList.HandleTypes(new ExEntityRemoveEventSender(entity, Descriptor, World.Dispatcher));
    }

    public void MoveIn(Entity entity, in TEntity data, IEntityHost source)
    {
        if (entity.GetCurrentState().Releasing) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        InnerHost.MoveIn(entity, data, source);
        entity.GetStateUnchecked().Host = this;
        if (source is IReactiveEntityHost reactiveSource) {
            Volatile.Read(ref _onEntityMovedIn)?.Invoke(entity, reactiveSource);
        }
    }

    public void MoveOut(Entity entity, IEntityHost destination)
    {
        if (entity.GetState().Releasing) {
            throw new InvalidOperationException("Cannot migrate an entity while it is being released.");
        }
        if (destination is IReactiveEntityHost reactiveDestination) {
            Volatile.Read(ref _onEntityMovedOut)?.Invoke(entity, reactiveDestination);
        }
        InnerHost.MoveOut(entity, destination);
    }

    public Entity GetEntity(int slot) => InnerHost.GetEntity(slot);
    public ref TEntity GetRef(int slot) => ref InnerHost.GetRef(slot);
    public ref byte GetByteRef(int slot) => ref InnerHost.GetByteRef(slot);

    public void GetHList<THandler>(int slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => InnerHost.GetHList(slot, handler);

    public object Box(int slot) => InnerHost.Box(slot);

    public Span<Entity> UnsafeGetEntitySpan() => InnerHost.UnsafeGetEntitySpan();
}
