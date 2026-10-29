#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using static WorldHostUtils;

public sealed class WorldEntityHost<TEntity, TInnerHost>(World world, TInnerHost innerHost)
    : IEntityHost<TEntity>, IReactiveEntityHost
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
    public event EntityMigrationHandler? OnEntityMovedOut;
    public event EntityMigrationHandler? OnEntityMovedIn;
    public event Action<IEntityHost>? OnDisposed;

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
        acquired = InnerHost.Create(initial);
        acquired.GetStateUnchecked().Host = this;
        World.Count++;
        OnEntityCreated?.Invoke(acquired);
        World.Dispatcher.Send(acquired, WorldEvents.Add.Instance);
        TEntity.HandleTypes(new EntityAddEventSender(acquired, World.Dispatcher));
        return acquired;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(Entity entity)
    {
        World.EnsureOwnerThread();
        var state = entity.GetState();
        if (!ReferenceEquals(state.Host, this)) { throw new InvalidOperationException("Entity belongs to another host."); }
        if (state.Releasing) { return; }
        state.Releasing = true;
        try {
            var dispatcher = World.Dispatcher;

            TEntity.HandleTypes(new EntityRemoveEventSender(entity, dispatcher));
            dispatcher.Send(entity, WorldEvents.Remove.Instance);
            dispatcher.UnlistenAll(entity);

            World.Count--;
            OnEntityReleased?.Invoke(entity);

            InnerHost.Release(entity);
        }
        finally { state.Releasing = false; }
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

    public void ReleaseOwned(Entity entity)
    {
        World.EnsureOwnerThread();
        var state = entity.GetState();
        if (!ReferenceEquals(state.Host, this)) {
            throw new InvalidOperationException("Owned output changed its host.");
        }
        if (state.Releasing) {
            return;
        }
        state.Releasing = true;
        try {
            var dispatcher = World.Dispatcher;
            var result = Outcome<Exception>.Success;
            try {
                TEntity.HandleTypes(new EntityRemoveEventSender(entity, dispatcher));
            }
            catch (Exception error) { result = result.Combine(Outcome<Exception>.Failure(error)); }
            try {
                dispatcher.Send(entity, WorldEvents.Remove.Instance);
            }
            catch (Exception error) { result = result.Combine(Outcome<Exception>.Failure(error)); }
            try {
                dispatcher.UnlistenAll(entity);
            }
            catch (Exception error) { result = result.Combine(Outcome<Exception>.Failure(error)); }
            World.Count--;
            try {
                OnEntityReleased?.Invoke(entity);
            }
            catch (Exception error) { result = result.Combine(Outcome<Exception>.Failure(error)); }
            try {
                InnerHost.Release(entity);
            }
            catch (Exception error) { result = result.Combine(Outcome<Exception>.Failure(error)); }
            result.ThrowIfFailed();
        }
        finally { state.Releasing = false; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add<TComponent>(Entity entity, in TComponent initial)
    {
        World.EnsureOwnerThread();
        InnerHost.Add(entity, initial);
        World.Dispatcher.Send(entity, WorldEvents.Add<TComponent>.Instance);
        World.Dispatcher.Send(entity, WorldEvents.Set<TComponent>.Instance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddMany<TList>(Entity entity, in TList list)
        where TList : struct, IHList
    {
        World.EnsureOwnerThread();
        InnerHost.AddMany(entity, list);
        TList.HandleTypes(new EntityAddEventSender(entity, World.Dispatcher));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<TComponent>(Entity entity, in TComponent value)
    {
        World.EnsureOwnerThread();
        InnerHost.Set(entity, value);
        World.Dispatcher.Send(entity, WorldEvents.Set<TComponent>.Instance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove<TComponent>(Entity entity, out bool success)
    {
        World.EnsureOwnerThread();
        InnerHost.Remove<TComponent>(entity, out success);
        if (success) {
            World.Dispatcher.Send(entity, WorldEvents.Remove<TComponent>.Instance);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveMany<TList>(Entity entity)
        where TList : struct, IHList
    {
        World.EnsureOwnerThread();
        InnerHost.RemoveMany<TList>(entity);
        TList.HandleTypes(new ExEntityRemoveEventSender(entity, Descriptor, World.Dispatcher));
    }

    public void MoveIn(Entity entity, in TEntity data, IEntityHost source)
    {
        InnerHost.MoveIn(entity, data, source);
        entity.GetStateUnchecked().Host = this;
        if (source is IReactiveEntityHost reactiveSource) {
            OnEntityMovedIn?.Invoke(entity, reactiveSource);
        }
    }

    public void MoveOut(Entity entity, IEntityHost destination)
    {
        if (destination is IReactiveEntityHost reactiveDestination) {
            OnEntityMovedOut?.Invoke(entity, reactiveDestination);
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
