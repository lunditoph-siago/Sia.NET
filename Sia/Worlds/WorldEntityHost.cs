namespace Sia;

using System.Runtime.CompilerServices;

public class WorldEntityHost<TEntity, TStorage>(
    World world, TStorage storage, IEntityHostProvider siblingHostProvider)
    : StorageEntityHost<TEntity, TStorage>(storage), IReactiveEntityHost
    where TEntity : IHList
    where TStorage : IStorage<HList<Identity, TEntity>>
{
    private struct EntityHeadAddEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler
    {
        public readonly void Handle<T>(in T value)
        {
            dispatcher.Send(entity, WorldEvents.Add<T>.Instance);
        }
    }

    private struct EntityTailAddEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
        {
            value.HandleHead(new EntityHeadAddEventSender(entity, dispatcher));
            value.HandleTail(new EntityTailAddEventSender(entity, dispatcher));
        }
    }

    private struct EntityHeadRemoveEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler
    {
        public readonly void Handle<T>(in T value)
        {
            dispatcher.Send(entity, WorldEvents.Remove<T>.Instance);
        }
    }

    private struct EntityTailRemoveEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
        {
            value.HandleHead(new EntityHeadRemoveEventSender(entity, dispatcher));
            value.HandleTail(new EntityTailRemoveEventSender(entity, dispatcher));
        }
    }

    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;

    public World World { get; } = world;

    protected override IEntityHost<UEntity> GetSiblingHost<UEntity>()
        => siblingHostProvider.GetHost<UEntity>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override EntityRef Create()
    {
        var entity = base.Create();
        var dispatcher = World.Dispatcher;

        World.Count++;
        OnEntityCreated?.Invoke(entity);
        dispatcher.Send(entity, WorldEvents.Add.Instance);

        ref var data = ref Storage.GetRef(entity.Slot);
        data.HandleHead(new EntityHeadAddEventSender(entity, dispatcher));
        data.HandleTail(new EntityTailAddEventSender(entity, dispatcher));

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
        data.HandleHead(new EntityHeadAddEventSender(entity, dispatcher));
        data.HandleTail(new EntityTailAddEventSender(entity, dispatcher));

        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Release(scoped in StorageSlot slot)
    {
        var entity = new EntityRef(slot, this);
        var dispatcher = World.Dispatcher;

        ref var data = ref Storage.GetRef(entity.Slot);
        data.HandleHead(new EntityHeadRemoveEventSender(entity, dispatcher));
        data.HandleTail(new EntityTailRemoveEventSender(entity, dispatcher));

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

    public override EntityRef AddBundle<TBundle>(in StorageSlot slot, in TBundle bundle)
    {
        var e = base.AddBundle(slot, bundle);
        bundle.HandleHead(new EntityHeadAddEventSender(e, World.Dispatcher));
        bundle.HandleTail(new EntityTailAddEventSender(e, World.Dispatcher));
        return e;
    }
}