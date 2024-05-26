#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia;

using System.Runtime.CompilerServices;

using static WorldHostUtils;

public class WorldEntityHost<TEntity, TStorage>(World world, TStorage storage)
    : StorageEntityHost<TEntity, TStorage>(storage), IReactiveEntityHost
    where TEntity : IHList
    where TStorage : IStorage<HList<Entity, TEntity>>, new()
{
    private unsafe readonly struct SiblingHostGetter<UEntity>(World world, IEntityHost<UEntity>* host)
        : IStorageTypeHandler<HList<Entity, UEntity>>
        where UEntity : IHList
    {
        public void Handle<UStorage>()
            where UStorage : IStorage<HList<Entity, UEntity>>, new()
            => *host = world.TryGetHost<WorldEntityHost<UEntity, UStorage>>(out var found)
                ? found : world.UnsafeAddRawHost(new WorldEntityHost<UEntity, UStorage>(world, new()));
    }

    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;

    public World World { get; } = world;

    public WorldEntityHost(World world) : this(world, new()) {}

    protected unsafe override IEntityHost<UEntity> GetSiblingHost<UEntity>()
    {
        IEntityHost<UEntity>? host = null;
        Storage.GetSiblingStorageType(new SiblingHostGetter<UEntity>(World, &host));
        return host!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Entity Create()
    {
        var entity = base.Create();
        var dispatcher = World.Dispatcher;

        World.Count++;
        OnEntityCreated?.Invoke(entity);
        dispatcher.Send(entity, WorldEvents.Add.Instance);

        TEntity.HandleTypes(new EntityAddEventSender(entity, dispatcher));
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Entity Create(in TEntity initial)
    {
        var entity = base.Create(initial);
        var dispatcher = World.Dispatcher;

        World.Count++;
        OnEntityCreated?.Invoke(entity);
        dispatcher.Send(entity, WorldEvents.Add.Instance);

        TEntity.HandleTypes(new EntityAddEventSender(entity, dispatcher));
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Release(in StorageSlot slot)
    {
        ref var entity = ref GetRef(slot).Head;
        var dispatcher = World.Dispatcher;

        TEntity.HandleTypes(new EntityRemoveEventSender(entity, dispatcher));
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        World.Count--;
        OnEntityReleased?.Invoke(entity);
        base.Release(slot);
    }

    public override Entity Add<TComponent>(in StorageSlot slot, in TComponent initial)
    {
        var e = base.Add(slot, initial);
        World.Dispatcher.Send(e, WorldEvents.Add<TComponent>.Instance);
        World.Dispatcher.Send(e, WorldEvents.Set<TComponent>.Instance);
        return e;
    }

    public override Entity AddMany<TList>(in StorageSlot slot, in TList list)
    {
        var e = base.AddMany(slot, list);
        TList.HandleTypes(new EntityAddEventSender(e, World.Dispatcher));
        return e;
    }

    public override Entity Set<TComponent>(in StorageSlot slot, in TComponent value)
    {
        var e = base.Set(slot, value);
        World.Dispatcher.Send(e, WorldEvents.Set<TComponent>.Instance);
        return e;
    }

    public override Entity Remove<TComponent>(in StorageSlot slot, out bool success)
    {
        var e = base.Remove<TComponent>(slot, out success);
        if (success) {
            World.Dispatcher.Send(e, WorldEvents.Remove<TComponent>.Instance);
        }
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Entity RemoveMany<TList>(in StorageSlot slot)
    {
        var e = base.RemoveMany<TList>(slot);
        TList.HandleTypes(new ExEntityRemoveEventSender(e, Descriptor, World.Dispatcher));
        return e;
    }
}