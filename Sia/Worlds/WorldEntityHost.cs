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

public sealed class WorldEntityHost<TEntity, TInnerHost>
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
            => *host = world.AcquireHost<UEntity, UInnerHost>();
    }

    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;
    private EntityMigrationHandler? _onEntityMovedOut;
    private EntityMigrationHandler? _onEntityMovedIn;
    private bool _disposed;
    private Outcome<Exception> _releaseResult;

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

    public World World { get; }
    public TInnerHost InnerHost { get; }

    public bool IsClosed => _disposed || InnerHost.IsClosed || World.IsClosingHost(this);
    internal bool IsDisposed => _disposed;

    public Type EntityType => InnerHost.EntityType;
    public EntityDescriptor Descriptor => InnerHost.Descriptor;

    public int Capacity => InnerHost.Capacity;
    public int Count => InnerHost.Count;
    public int Version => InnerHost.Version;

    public bool TryGetSequentialBytes(out Span<byte> bytes)
        => InnerHost.TryGetSequentialBytes(out bytes);

    public WorldEntityHost(World world, TInnerHost innerHost)
    {
        World = world;
        innerHost.OnInitialize(world);
        InnerHost = innerHost;
    }

    public WorldEntityHost(World world)
    {
        World = world;
        var inner = new TInnerHost();
        try {
            inner.OnInitialize(world);
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error).Attempt(inner.Dispose).ThrowIfFailed();
            throw;
        }
        InnerHost = inner;
    }

    public unsafe IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : struct, IHList
    {
        if (typeof(TInnerHost) == typeof(ArrayEntityHost<TEntity>)
            && InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)) {
            return World.AcquireHost<UEntity, ArrayEntityHost<UEntity>>();
        }
        if (typeof(TInnerHost) == typeof(UnmanagedArrayEntityHost<TEntity>)
            && InnerHost.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>)) {
            return World.AcquireHost<UEntity, UnmanagedArrayEntityHost<UEntity>>();
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
        if (_disposed) return;
        if (!World.IsDisposed && World.Actor is not { IsCompleted: true }) {
            World.EnsureOwnerThread();
        }
        if (World.HostCallbackDepth != 0 || World.Dispatcher.IsSending) {
            throw new InvalidOperationException("Cannot remove a host during entity callbacks or event dispatch.");
        }
        _disposed = true;
        var result = Outcome<Exception>.Success
            .Attempt(this, static (in host) => {
                var dispatcher = host.World.Dispatcher;
                foreach (var entity in host.InnerHost.UnsafeGetEntitySpan()) {
                    if (dispatcher.HasNoUntypedListeners) break;
                    dispatcher.DetachEntityListeners(entity);
                }
            })
            .Attempt(this, static (in host) => host.World.Count -= host.InnerHost.Count)
            .Attempt(InnerHost, static (in host) => host.Dispose());
        foreach (var observer in Delegate.EnumerateInvocationList(OnDisposed)) {
            result = result.Attempt((observer, host: this), static (in item) => item.observer(item.host));
        }
        result.ThrowIfFailed();
    }

    public Entity Create() => Create(default!);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Create(in TEntity initial) => Create(initial, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Create(in TEntity initial, out Entity acquired)
    {
        World.EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsClosed, this);
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
    public void Release(Entity entity)
    {
        World.EnsureOwnerThread();
        var state = entity.GetState();
        if (!ReferenceEquals(state.Host, this)) {
            throw new InvalidOperationException("Entity belongs to another host.");
        }
        if (state.NotifyingMigration) {
            throw new InvalidOperationException("Cannot release an entity during a migration callback.");
        }
        if (state.Releasing) {
            return;
        }
        if (World.Dispatcher.IsSending) {
            DeferRelease(entity);
            state.Releasing = true;
            return;
        }
        state.Releasing = true;
        Release(entity, state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DeferRelease(Entity entity)
        => World.Dispatcher.RunAfterSend(() => {
            if (!entity.IsValid) return;
            var state = entity.GetStateUnchecked();
            if (!ReferenceEquals(state.Host, this)) {
                state.Releasing = false;
                throw new InvalidOperationException("Entity changed its host while release was pending.");
            }
            Release(entity, state);
        });

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

    private void Release(Entity entity, EntityState state)
    {
        var previousResult = _releaseResult;
        _releaseResult = Outcome<Exception>.Success;
        World.HostCallbackDepth++;
        try {
            var dispatcher = World.Dispatcher;
            try {
                TEntity.HandleTypes(new ReleaseEventSender(this, entity));
            }
            catch (Exception error) {
                RecordReleaseError(error);
            }
            try {
                if (entity.GetStateUnchecked().EventSink is not null || !dispatcher.HasNoListeners<WorldEvents.Remove>()) {
                    dispatcher.SendOwned(entity, WorldEvents.Remove.Instance);
                }
            }
            catch (Exception error) {
                RecordReleaseError(error);
            }
            try {
                dispatcher.DetachEntityListeners(entity);
            }
            catch (Exception error) {
                RecordReleaseError(error);
            }

            World.Count--;
            var observers = OnEntityReleased;
            if (observers is { HasSingleTarget: true }) {
                try {
                    observers(entity);
                }
                catch (Exception error) {
                    RecordReleaseError(error);
                }
            }
            else if (observers is { }) {
                foreach (var observer in Delegate.EnumerateInvocationList(observers)) {
                    try {
                        observer(entity);
                    }
                    catch (Exception error) {
                        RecordReleaseError(error);
                    }
                }
            }
            try {
                InnerHost.Release(entity);
            }
            catch (Exception error) {
                RecordReleaseError(error);
            }
            _releaseResult.ThrowIfFailed();
        }
        finally {
            _releaseResult = default;
            if (!previousResult.IsSuccess) _releaseResult = previousResult;
            World.HostCallbackDepth--;
            if (entity.References(state)) state.Releasing = false;
        }
    }

    private void RecordReleaseError(Exception error)
        => _releaseResult = _releaseResult.Combine(Outcome<Exception>.Failure(error));

    private readonly struct ReleaseEventSender(WorldEntityHost<TEntity, TInnerHost> host, Entity entity) : IGenericTypeHandler
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Handle<T>()
        {
            var dispatcher = host.World.Dispatcher;
            if (entity.GetStateUnchecked().EventSink is null && dispatcher.HasNoListeners<WorldEvents.Remove<T>>()) return;
            Send<T>(dispatcher);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Send<T>(WorldDispatcher dispatcher)
        {
            try {
                dispatcher.SendOwned(entity, WorldEvents.Remove<T>.Instance);
            }
            catch (Exception error) {
                host.RecordReleaseError(error);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add<TComponent>(Entity entity, in TComponent initial)
    {
        World.EnsureOwnerThread();
        var state = GetEntityState(entity);
        if (state.IsTransitioning) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        if (typeof(TInnerHost) == typeof(ArrayEntityHost<TEntity>)
            && InnerHost is ArrayEntityHost<TEntity> managed && managed.GetType() == typeof(ArrayEntityHost<TEntity>))
            managed.AddDefault(entity, state, initial);
        else if (typeof(TInnerHost) == typeof(UnmanagedArrayEntityHost<TEntity>)
            && InnerHost is UnmanagedArrayEntityHost<TEntity> unmanaged && unmanaged.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>))
            unmanaged.AddDefault(entity, state, initial);
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
        if (IsClosed || !World.HasNoBatchObservers()) return false;
        foreach (var entity in entities) {
            var state = entity.GetStateUnchecked();
            if (state is null || !entity.References(state) || !ReferenceEquals(state.Host, this)
                || state.IsTransitioning || state.EventSink is not null) return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddMany<TList>(Entity entity, in TList list)
        where TList : struct, IHList
    {
        World.EnsureOwnerThread();
        if (GetEntityState(entity).IsTransitioning) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        InnerHost.AddMany(entity, list);
        TList.HandleTypes(new EntityAddEventSender(entity, World.Dispatcher));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<TComponent>(Entity entity, in TComponent value)
    {
        World.EnsureOwnerThread();
        var state = GetEntityState(entity);
        if (state.IsTransitioning && !Descriptor.Contains<TComponent>()) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        InnerHost.Set(entity, value);
        World.Dispatcher.Send(entity, WorldEvents.Set<TComponent>.Instance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove<TComponent>(Entity entity, out bool success)
    {
        World.EnsureOwnerThread();
        var state = GetEntityState(entity);
        if (state.IsTransitioning && Descriptor.Contains<TComponent>()) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        if (typeof(TInnerHost) == typeof(ArrayEntityHost<TEntity>)
            && InnerHost is ArrayEntityHost<TEntity> managed && managed.GetType() == typeof(ArrayEntityHost<TEntity>))
            managed.RemoveDefault<TComponent>(entity, state, out success);
        else if (typeof(TInnerHost) == typeof(UnmanagedArrayEntityHost<TEntity>)
            && InnerHost is UnmanagedArrayEntityHost<TEntity> unmanaged && unmanaged.GetType() == typeof(UnmanagedArrayEntityHost<TEntity>))
            unmanaged.RemoveDefault<TComponent>(entity, state, out success);
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
        var state = GetEntityState(entity);
        if (state.IsTransitioning) {
            foreach (var component in EntityDescriptor<TList>.Components) {
                if (Descriptor.Offsets.ContainsKey(component.Type)) {
                    throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
                }
            }
            return;
        }
        InnerHost.RemoveMany<TList>(entity);
        TList.HandleTypes(new ExEntityRemoveEventSender(entity, Descriptor, World.Dispatcher));
    }

    public void MoveIn(Entity entity, in TEntity data, IEntityHost source)
    {
        World.EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsClosed, this);
        var state = entity.GetCurrentState();
        if (state.IsTransitioning) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        if (!ReferenceEquals(state.Host, source) || !ReferenceEquals(source.World, World)) {
            throw new InvalidOperationException("Migration source does not own the entity or belongs to another world.");
        }
        InnerHost.MoveIn(entity, data, source);
        state.Host = this;
        if (source is IReactiveEntityHost reactive) NotifyMigration(entity, state, reactive, incoming: true);
    }

    public void MoveOut(Entity entity, IEntityHost destination)
    {
        World.EnsureOwnerThread();
        var state = GetEntityState(entity);
        if (state.IsTransitioning) {
            throw new InvalidOperationException("Cannot migrate an entity during release or a migration callback.");
        }
        if (!ReferenceEquals(destination.World, World)) {
            throw new InvalidOperationException("Migration destination belongs to another world.");
        }
        ObjectDisposedException.ThrowIf(destination.IsClosed, destination);
        if (destination is IReactiveEntityHost reactive) NotifyMigration(entity, state, reactive, incoming: false);
        InnerHost.MoveOut(entity, destination);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool NotifyMigration(Entity entity, EntityState state, IReactiveEntityHost other, bool incoming)
    {
        var observer = incoming ? Volatile.Read(ref _onEntityMovedIn) : Volatile.Read(ref _onEntityMovedOut);
        if (observer is null) return false;
        state.NotifyingMigration = true;
        World.HostCallbackDepth++;
        try {
            observer(entity, other);
        }
        finally {
            World.HostCallbackDepth--;
            if (entity.References(state)) state.NotifyingMigration = false;
        }
        if (!incoming && (!entity.References(state) || !ReferenceEquals(state.Host, this))) {
            throw new InvalidOperationException("Entity changed its host during its migration callback.");
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntityState GetEntityState(Entity entity)
    {
        var state = entity.GetState();
        if (!ReferenceEquals(state.Host, this)) throw new InvalidOperationException("Entity belongs to another host.");
        return state;
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
