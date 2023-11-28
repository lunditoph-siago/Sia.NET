
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
    public void Release(long pointer)
    {
        var entity = new EntityRef(pointer, this);

        var dispatcher = World.Dispatcher;
        World.Count--;
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        OnEntityReleased?.Invoke(entity);
        _host.Release(pointer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>(long pointer)
        => _host.Contains<TComponent>(pointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(long pointer, Type type)
        => _host.Contains(pointer, type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>(long pointer)
        => ref _host.Get<TComponent>(pointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetOrNullRef<TComponent>(long pointer)
        => ref _host.GetOrNullRef<TComponent>(pointer);

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
    public object Box(long pointer)
        => _host.Box(pointer);
}