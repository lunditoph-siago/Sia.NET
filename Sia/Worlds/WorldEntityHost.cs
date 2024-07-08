#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using static WorldHostUtils;

public sealed class WorldEntityHost<TEntity, TInnerHost>(World world, TInnerHost innerHost)
    : IEntityHost<TEntity>, IReactiveEntityHost
    where TEntity : IHList
    where TInnerHost : IEntityHost<TEntity>, new()
{
    private unsafe readonly struct SiblingInnerHostGetter<UEntity>(World world, IEntityHost<UEntity>* host)
        : IGenericConcreteTypeHandler<IEntityHost<UEntity>>
        where UEntity : IHList
    {
        public void Handle<UInnerHost>()
            where UInnerHost : IEntityHost<UEntity>, new()
            => *host = world.TryGetHost<WorldEntityHost<UEntity, UInnerHost>>(out var found)
                ? found : world.UnsafeAddRawHost(new WorldEntityHost<UEntity, UInnerHost>(world, new()));
    }

    public event EntityHandler? OnEntityCreated;
    public event EntityHandler? OnEntityReleased;
    public event Action<IEntityHost>? OnDisposed;

    public World World { get; } = world;
    public TInnerHost InnerHost { get; } = innerHost;

    public Type EntityType => InnerHost.EntityType;
    public EntityDescriptor Descriptor => InnerHost.Descriptor;

    public int Capacity => InnerHost.Capacity;
    public int Count => InnerHost.Count;

    public WorldEntityHost(World world) : this(world, new()) {}

    public unsafe IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : IHList
    {
        IEntityHost<UEntity>? host = null;
        InnerHost.GetSiblingHostType(new SiblingInnerHostGetter<UEntity>(World, &host));
        return host!;
    }

    public void GetSiblingHostType<UEntity>(IGenericConcreteTypeHandler<IEntityHost<UEntity>> hostTypeHandler)
        where UEntity : IHList
        => throw new NotSupportedException("Cannot get concrete sibling type for world hosts");

    public IEnumerator<Entity> GetEnumerator() => InnerHost.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => InnerHost.GetEnumerator();

    public void Dispose()
    {
        InnerHost.Dispose();
        OnDisposed?.Invoke(this);
    }

    public Entity Create() => Create(default!);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Create(in TEntity initial)
    {
        var entity = InnerHost.Create(initial);
        entity.Host = this;

        World.Count++;
        OnEntityCreated?.Invoke(entity);

        var dispatcher = World.Dispatcher;
        dispatcher.Send(entity, WorldEvents.Add.Instance);
        TEntity.HandleTypes(new EntityAddEventSender(entity, dispatcher));
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(int slot)
    {
        var entity = InnerHost.GetEntity(slot);
        var dispatcher = World.Dispatcher;

        TEntity.HandleTypes(new EntityRemoveEventSender(entity, dispatcher));
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        World.Count--;
        OnEntityReleased?.Invoke(entity);

        InnerHost.Release(slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Add<TComponent>(int slot, in TComponent initial)
    {
        var e = InnerHost.Add(slot, initial);
        World.Dispatcher.Send(e, WorldEvents.Add<TComponent>.Instance);
        World.Dispatcher.Send(e, WorldEvents.Set<TComponent>.Instance);
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity AddMany<TList>(int slot, in TList list)
        where TList : IHList
    {
        var e = InnerHost.AddMany(slot, list);
        TList.HandleTypes(new EntityAddEventSender(e, World.Dispatcher));
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Set<TComponent>(int slot, in TComponent value)
    {
        var e = InnerHost.Set(slot, value);
        World.Dispatcher.Send(e, WorldEvents.Set<TComponent>.Instance);
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Remove<TComponent>(int slot, out bool success)
    {
        var e = InnerHost.Remove<TComponent>(slot, out success);
        if (success) {
            World.Dispatcher.Send(e, WorldEvents.Remove<TComponent>.Instance);
        }
        return e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity RemoveMany<TList>(int slot)
        where TList : IHList
    {
        var e = InnerHost.RemoveMany<TList>(slot);
        TList.HandleTypes(new ExEntityRemoveEventSender(e, Descriptor, World.Dispatcher));
        return e;
    }

    public void MoveIn(Entity entity, in TEntity data)
    {
        InnerHost.MoveIn(entity, data);
        entity.Host = this;
    }

    public ref TEntity GetRef(int slot) => ref InnerHost.GetRef(slot);
    public ref TEntity GetRef(int slot, out Entity entity) => ref InnerHost.GetRef(slot, out entity);

    public void MoveOut(int slot) => InnerHost.MoveOut(slot);
    public Entity GetEntity(int slot) => InnerHost.GetEntity(slot);

    public ref byte GetByteRef(int slot) => ref InnerHost.GetByteRef(slot);
    public ref byte GetByteRef(int slot, out Entity entity) => ref InnerHost.GetByteRef(slot, out entity);

    public void GetHList<THandler>(int slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => InnerHost.GetHList(slot, handler);

    public object Box(int slot) => InnerHost.Box(slot);
}