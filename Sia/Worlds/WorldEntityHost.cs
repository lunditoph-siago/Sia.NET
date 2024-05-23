#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia;

using System.Runtime.CompilerServices;

using static WorldHostUtils;

public class WorldEntityHost<TEntity, TStorage>(World world, TStorage storage)
    : StorageEntityHost<TEntity, TStorage>(storage), IReactiveEntityHost
    where TEntity : IHList
    where TStorage : IStorage<HList<Identity, TEntity>>, new()
{
    private unsafe readonly struct SiblingHostGetter<UEntity>(World world, IEntityHost<UEntity>* host)
        : IStorageTypeHandler<HList<Identity, UEntity>>
        where UEntity : IHList
    {
        public void Handle<UStorage>()
            where UStorage : IStorage<HList<Identity, UEntity>>, new()
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
    public override EntityRef Create()
    {
        var entity = base.Create();
        var dispatcher = World.Dispatcher;

        World.Count++;
        OnEntityCreated?.Invoke(entity);
        dispatcher.Send(entity, WorldEvents.Add.Instance);

        ref var data = ref Storage.GetRef(entity.Slot);
        new EntityAddEventSender(entity, dispatcher).Handle(data);

        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef Create(in TEntity initial)
    {
        var entity = base.Create(initial);
        var dispatcher = World.Dispatcher;

        World.Count++;
        OnEntityCreated?.Invoke(entity);
        dispatcher.Send(entity, WorldEvents.Add.Instance);

        ref var data = ref Storage.GetRef(entity.Slot);
        new EntityAddEventSender(entity, dispatcher).Handle(data);

        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Release(in StorageSlot slot)
    {
        var entity = new EntityRef(slot, this);
        var dispatcher = World.Dispatcher;

        ref var data = ref Storage.GetRef(entity.Slot);
        TEntity.HandleTypes(new EntityRemoveEventSender(entity, dispatcher));

        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        World.Count--;
        OnEntityReleased?.Invoke(entity);
        base.Release(slot);
    }

    public override EntityRef MoveIn(in HList<Identity, TEntity> data)
    {
        var entity = base.MoveIn(data);
        OnEntityCreated?.Invoke(entity);
        return entity;
    }

    public override void MoveOut(in StorageSlot slot)
    {
        OnEntityReleased?.Invoke(new(slot, this));
        base.MoveOut(slot);
    }

    public override EntityRef Add<TComponent>(in StorageSlot slot, in TComponent initial)
    {
        var e = base.Add(slot, initial);
        World.Dispatcher.Send(e, WorldEvents.Add<TComponent>.Instance);
        return e;
    }

    public override EntityRef AddMany<TBundle>(in StorageSlot slot, in TBundle bundle)
    {
        var e = base.AddMany(slot, bundle);
        new EntityAddEventSender(e, World.Dispatcher).Handle(bundle);
        return e;
    }

    public override EntityRef Remove<TComponent>(in StorageSlot slot)
    {
        var e = base.Remove<TComponent>(slot);
        World.Dispatcher.Send(e, WorldEvents.Remove<TComponent>.Instance);
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef RemoveMany<TList>(in StorageSlot slot)
    {
        var e = base.RemoveMany<TList>(slot);
        TList.HandleTypes(new EntityRemoveEventSender(e, World.Dispatcher));
        return e;
    }
}