namespace Sia;

using System;
using System.Runtime.CompilerServices;

public sealed class WorldEntityHost<T, TStorage>
    : Internal.EntityHost<T, WrappedStorage<T, TStorage>>, IReactiveEntityHost
    where T : struct
    where TStorage : class, IStorage<T>
{
    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;

    public World World { get; }

    public WorldEntityHost(World world, TStorage storage)
        : base(new(storage))
    {
        World = world;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef Create()
    {
        var entity = base.Create();
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef Create(in T initial)
    {
        var entity = base.Create(initial);
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Release(long pointer)
    {
        var entity = new EntityRef(pointer, this);
        World.Dispatcher.Send(entity, WorldEvents.Remove.Instance);
        World.Dispatcher.UnlistenAll(entity);
        OnEntityReleased?.Invoke(entity);
        base.Release(pointer);
    }
}