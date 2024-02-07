namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public sealed class WorldEntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T, TStorage>(World world, TStorage storage)
    : EntityHost<T, TStorage>(storage), IReactiveEntityHost
    where T : struct
    where TStorage : IStorage<T>
{
    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;

    public World World { get; } = world;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef<T> Create()
    {
        var entity = base.Create();
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef<T> Create(in T initial)
    {
        var entity = base.Create(initial);
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Release(nint pointer, int version)
    {
        var entity = new EntityRef(pointer, version, this);

        var dispatcher = World.Dispatcher;
        World.Count--;
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        OnEntityReleased?.Invoke(entity);
        base.Release(pointer, version);
    }
}