
namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;

public record WrappedWorldEntityHost<T, TEntityHost> : IEntityHost<T>, IReactiveEntityHost
    where T : struct
    where TEntityHost : IEntityHost<T>
{
    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;

    public World World { get; }
    public TEntityHost InnerHost => _host;

    public EntityDescriptor Descriptor {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _host.Descriptor;
    } 

    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _host.Capacity;
    }

    public int Count {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _host.Count;
    }

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
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef Create(in T initial)
    {
        var entity = _host.Create(initial);
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(nint pointer, int version)
    {
        _host.Release(pointer, version);
        var entity = new EntityRef(pointer, version, this);

        var dispatcher = World.Dispatcher;
        World.Count--;
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        OnEntityReleased?.Invoke(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>(nint pointer, int version)
        => _host.Contains<TComponent>(pointer, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(nint pointer, int version, Type type)
        => _host.Contains(pointer, version, type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>(nint pointer, int version)
        => ref _host.Get<TComponent>(pointer, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetOrNullRef<TComponent>(nint pointer, int version)
        => ref _host.GetOrNullRef<TComponent>(pointer, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
        => _host.IterateAllocated(handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
        => _host.IterateAllocated(data, handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<EntityRef> GetEnumerator()
        => _host.GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerator IEnumerable.GetEnumerator()
        => _host.GetEnumerator();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object Box(nint pointer, int version)
        => _host.Box(pointer, version);
}