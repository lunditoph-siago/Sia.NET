#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace Sia;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using static WorldHostUtils;

public sealed class WorldEntityHost<TEntity, TInnerHost>(World world, TInnerHost innerHost)
    : IEntityHost<TEntity>, IReactiveEntityHost
    where TEntity : struct, IHList
    where TInnerHost : IEntityHost<TEntity>, new()
{
    private unsafe readonly struct SiblingInnerHostGetter<UEntity>(World world, IEntityHost<UEntity>* host)
        : IGenericConcreteTypeHandler<IEntityHost<UEntity>>
        where UEntity : struct, IHList
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
    public int Version => InnerHost.Version;

    public WorldEntityHost(World world) : this(world, new()) {}

    public unsafe IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : struct, IHList
    {
        IEntityHost<UEntity>? host = null;
        InnerHost.GetSiblingHostType(new SiblingInnerHostGetter<UEntity>(World, &host));
        return host!;
    }

    public void GetSiblingHostType<UEntity>(IGenericConcreteTypeHandler<IEntityHost<UEntity>> hostTypeHandler)
        where UEntity : struct, IHList
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
    public void Release(Entity entity)
    {
        var dispatcher = World.Dispatcher;

        TEntity.HandleTypes(new EntityRemoveEventSender(entity, dispatcher));
        dispatcher.Send(entity, WorldEvents.Remove.Instance);
        dispatcher.UnlistenAll(entity);

        World.Count--;
        OnEntityReleased?.Invoke(entity);

        InnerHost.Release(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add<TComponent>(Entity entity, in TComponent initial)
    {
        InnerHost.Add(entity, initial);
        World.Dispatcher.Send(entity, WorldEvents.Add<TComponent>.Instance);
        World.Dispatcher.Send(entity, WorldEvents.Set<TComponent>.Instance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddMany<TList>(Entity entity, in TList list)
        where TList : struct, IHList
    {
        InnerHost.AddMany(entity, list);
        TList.HandleTypes(new EntityAddEventSender(entity, World.Dispatcher));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<TComponent>(Entity entity, in TComponent value)
    {
        InnerHost.Set(entity, value);
        World.Dispatcher.Send(entity, WorldEvents.Set<TComponent>.Instance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove<TComponent>(Entity entity, out bool success)
    {
        InnerHost.Remove<TComponent>(entity, out success);
        if (success) {
            World.Dispatcher.Send(entity, WorldEvents.Remove<TComponent>.Instance);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveMany<TList>(Entity entity)
        where TList : struct, IHList
    {
        InnerHost.RemoveMany<TList>(entity);
        TList.HandleTypes(new ExEntityRemoveEventSender(entity, Descriptor, World.Dispatcher));
    }

    public void MoveIn(Entity entity, in TEntity data)
    {
        InnerHost.MoveIn(entity, data);
        entity.Host = this;
    }

    public void MoveOut(Entity entity) => InnerHost.MoveOut(entity);

    public Entity GetEntity(int slot) => InnerHost.GetEntity(slot);
    public ref TEntity GetRef(int slot) => ref InnerHost.GetRef(slot);
    public ref byte GetByteRef(int slot) => ref InnerHost.GetByteRef(slot);

    public void GetHList<THandler>(int slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => InnerHost.GetHList(slot, handler);

    public object Box(int slot) => InnerHost.Box(slot);

    public Span<Entity> UnsafeGetEntitySpan() => InnerHost.UnsafeGetEntitySpan();
}