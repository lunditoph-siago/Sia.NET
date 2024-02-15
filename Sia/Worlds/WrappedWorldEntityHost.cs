
namespace Sia;

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public sealed record WrappedWorldEntityHost<T, TEntityHost> : IEntityHost<T>, IReactiveEntityHost
    where T : struct
    where TEntityHost : IEntityHost<T>
{
    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;

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
    public bool ContainsCommon<TComponent>()
        => _host.ContainsCommon<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsCommon(Type componentType)
        => _host.ContainsCommon(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    EntityRef IEntityHost.Create() => _host.Create();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef<T> Create()
    {
        var entity = _host.Create();
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef<T> Create(in T initial)
    {
        var entity = _host.Create(initial);
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(int slot, int version)
    {
        _host.Release(slot, version);
        var entity = new EntityRef(slot, version, this);

        var dispatcher = World.Dispatcher;
        World.Count--;
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        OnEntityReleased?.Invoke(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(int slot, int version)
        => _host.IsValid(slot, version);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>(int slot, int version)
        => _host.ContainsCommon<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int slot, int version, Type componentType)
        => _host.ContainsCommon(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityDescriptor GetDescriptor(int slot, int version)
        => _host.GetDescriptor(slot, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>(int slot, int version)
        => ref _host.Get<TComponent>(slot, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetOrNullRef<TComponent>(int slot, int version)
        => ref _host.GetOrNullRef<TComponent>(slot, version);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object Box(int slot, int version)
        => _host.Box(slot, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int slot, int version)
        => _host.GetSpan(slot, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<EntityRef> GetEnumerator() => _host.GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerator IEnumerable.GetEnumerator() => _host.GetEnumerator();

    public void Dispose() => _host.Dispose();
}