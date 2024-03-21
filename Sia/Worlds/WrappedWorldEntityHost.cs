
namespace Sia;

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public sealed record WrappedWorldEntityHost<TEntity, TEntityHost> : IEntityHost<TEntity>, IReactiveEntityHost
    where TEntity : struct
    where TEntityHost : IEntityHost<TEntity>
{
    public event Action? OnDisposed {
        add => _host.OnDisposed += value;
        remove => _host.OnDisposed -= value;
    }

    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;

    public EntityDescriptor Descriptor => _host.Descriptor;

    public World World { get; }
    public TEntityHost InnerHost => _host;

    public int Capacity => _host.Capacity;
    public int Count => _host.Count;
    public ReadOnlySpan<StorageSlot> AllocatedSlots => _host.AllocatedSlots;

    private readonly TEntityHost _host;

    public WrappedWorldEntityHost(World world, TEntityHost host)
    {
        World = world;
        _host = host;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    EntityRef IEntityHost.Create() => _host.Create();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef<WithId<TEntity>> Create()
    {
        var entity = _host.Create();
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef<WithId<TEntity>> Create(in TEntity initial)
    {
        var entity = _host.Create(initial);
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(scoped in StorageSlot slot)
    {
        _host.Release(slot);
        var entity = new EntityRef(slot, this);

        var dispatcher = World.Dispatcher;
        World.Count--;
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        OnEntityReleased?.Invoke(entity);
    }

    public bool IsValid(scoped in StorageSlot slot)
        => _host.IsValid(slot);

    public ref byte GetByteRef(scoped in StorageSlot slot)
        => ref _host.GetByteRef(slot);

    public ref byte UnsafeGetByteRef(scoped in StorageSlot slot)
        => ref _host.UnsafeGetByteRef(slot);

    public ref WithId<TEntity> GetRef(scoped in StorageSlot slot)
        => ref _host.GetRef(slot);

    public ref WithId<TEntity> UnsafeGetRef(scoped in StorageSlot slot)
        => ref _host.UnsafeGetRef(slot);
    
    public object Box(scoped in StorageSlot slot)
        => _host.Box(slot);

    public IEnumerator<EntityRef> GetEnumerator() => _host.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _host.GetEnumerator();

    public void Dispose() => _host.Dispose();
}