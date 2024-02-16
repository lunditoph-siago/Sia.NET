
namespace Sia;

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

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
    public void Release(StorageSlot slot)
    {
        _host.Release(slot);
        var entity = new EntityRef(slot, this);

        var dispatcher = World.Dispatcher;
        World.Count--;
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        OnEntityReleased?.Invoke(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(StorageSlot slot)
        => _host.IsValid(slot);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>(StorageSlot slot)
        => _host.ContainsCommon<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(StorageSlot slot, Type componentType)
        => _host.ContainsCommon(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityDescriptor GetDescriptor(StorageSlot slot)
        => _host.GetDescriptor(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>(StorageSlot slot)
        => ref _host.Get<TComponent>(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetOrNullRef<TComponent>(StorageSlot slot)
        => ref _host.GetOrNullRef<TComponent>(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanOwner<T> Fetch(ReadOnlySpan<StorageSlot> slots)
        => _host.Fetch(slots);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanOwner<T> UnsafeFetch(ReadOnlySpan<StorageSlot> slots)
        => _host.UnsafeFetch(slots);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<StorageSlot> slots, ReadOnlySpan<T> values)
        => _host.Write(slots, values);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeWrite(ReadOnlySpan<StorageSlot> slots, ReadOnlySpan<T> values)
        => _host.UnsafeWrite(slots, values);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object Box(StorageSlot slot)
        => _host.Box(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(StorageSlot slot)
        => _host.GetSpan(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<EntityRef> GetEnumerator() => _host.GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerator IEnumerable.GetEnumerator() => _host.GetEnumerator();

    public void Dispose() => _host.Dispose();
}