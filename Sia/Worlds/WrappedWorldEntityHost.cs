
namespace Sia;

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using static WorldHostUtils;

public sealed record WrappedWorldEntityHost<TEntity, TEntityHost> : IEntityHost<TEntity>, IReactiveEntityHost
    where TEntity : IHList
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
    public EntityRef Create()
    {
        var entity = _host.Create();
        var dispatcher = World.Dispatcher;

        World.Count++;
        OnEntityCreated?.Invoke(entity);
        dispatcher.Send(entity, WorldEvents.Add.Instance);

        ref var data = ref _host.GetRef(entity.Slot);
        data.HandleHead(new EntityHeadAddEventSender(entity, dispatcher));
        data.HandleTail(new EntityTailAddEventSender(entity, dispatcher));

        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef Create(in TEntity initial)
    {
        var entity = _host.Create(initial);
        var dispatcher = World.Dispatcher;

        World.Count++;
        OnEntityCreated?.Invoke(entity);
        dispatcher.Send(entity, WorldEvents.Add.Instance);

        ref var data = ref _host.GetRef(entity.Slot);
        data.HandleHead(new EntityHeadAddEventSender(entity, dispatcher));
        data.HandleTail(new EntityTailAddEventSender(entity, dispatcher));

        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(in StorageSlot slot)
    {
        var entity = new EntityRef(slot, this);
        var dispatcher = World.Dispatcher;

        ref var data = ref _host.GetRef(entity.Slot);
        data.HandleHead(new EntityHeadRemoveEventSender(entity, dispatcher));
        data.HandleTail(new EntityTailRemoveEventSender(entity, dispatcher));

        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        World.Count--;
        OnEntityReleased?.Invoke(entity);
        _host.Release(slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef MoveIn(in HList<Identity, TEntity> data)
    {
        var entity = _host.MoveIn(data);
        OnEntityCreated?.Invoke(entity);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MoveOut(in StorageSlot slot)
    {
        OnEntityReleased?.Invoke(new(slot, this));
        _host.MoveOut(slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef Add<TComponent>(in StorageSlot slot, in TComponent initial)
        => _host.Add(slot, initial);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef AddMany<TBundle>(in StorageSlot slot, in TBundle bundle)
        where TBundle : IHList
    {
        var e = _host.AddMany(slot, bundle);
        bundle.HandleHead(new EntityHeadAddEventSender(e, World.Dispatcher));
        bundle.HandleTail(new EntityTailAddEventSender(e, World.Dispatcher));
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef Remove<TComponent>(in StorageSlot slot)
        => _host.Remove<TComponent>(slot);

    public bool IsValid(in StorageSlot slot)
        => _host.IsValid(slot);

    public unsafe ref byte GetByteRef(in StorageSlot slot)
        => ref _host.GetByteRef(slot);

    public unsafe ref byte UnsafeGetByteRef(in StorageSlot slot)
        => ref _host.UnsafeGetByteRef(slot);

    public ref HList<Identity, TEntity> GetRef(in StorageSlot slot)
        => ref _host.GetRef(slot);

    public ref HList<Identity, TEntity> UnsafeGetRef(in StorageSlot slot)
        => ref _host.UnsafeGetRef(slot);
    
    public object Box(in StorageSlot slot)
        => _host.Box(slot);

    public IEnumerator<EntityRef> GetEnumerator() => _host.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _host.GetEnumerator();

    public void Dispose() => _host.Dispose();
}