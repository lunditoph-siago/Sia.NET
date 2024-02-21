namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public sealed class WorldEntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, TStorage>(World world, TStorage storage)
    : StorageEntityHost<TEntity, TStorage>(storage), IReactiveEntityHost
    where TEntity : struct
    where TStorage : IStorage<TEntity>
{
    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;

    public World World { get; } = world;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef<TEntity> Create()
    {
        var entity = base.Create();
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef<TEntity> Create(in TEntity initial)
    {
        var entity = base.Create(initial);
        World.Count++;
        OnEntityCreated?.Invoke(entity);
        World.Dispatcher.Send(entity, WorldEvents.Add.Instance);
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Release(scoped in StorageSlot slot)
    {
        var entity = new EntityRef(slot, this);

        var dispatcher = World.Dispatcher;
        World.Count--;
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        OnEntityReleased?.Invoke(entity);
        base.Release(slot);
    }
}