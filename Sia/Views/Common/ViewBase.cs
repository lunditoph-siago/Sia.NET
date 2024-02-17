namespace Sia;

using System.Diagnostics.CodeAnalysis;

public abstract class ViewBase : IAddon
{
    protected delegate void ForeverListener<UEvent>(in EntityRef target, in UEvent e)
        where UEvent : IEvent;

    private event Action? OnUnlisten;

    [AllowNull]
    public World World { get; private set; }

    protected void Listen(IEventListener<EntityRef> listener)
    {
        World.Dispatcher.Listen(listener);
        OnUnlisten += () => World.Dispatcher.Unlisten(listener);
    }

    protected void Listen<UEvent>(WorldDispatcher.Listener<UEvent> listener)
        where UEvent : IEvent
    {
        World.Dispatcher.Listen(listener);
        OnUnlisten += () => World.Dispatcher.Unlisten(listener);
    }

    protected void Listen<UEvent>(ForeverListener<UEvent> listener)
        where UEvent : IEvent
    {
        bool Listener(in EntityRef entity, in UEvent command)
        {
            listener(entity, command);
            return false;
        }
        World.Dispatcher.Listen<UEvent>(Listener);
        OnUnlisten += () => World.Dispatcher.Unlisten<UEvent>(Listener);
    }

    protected void Listen(EntityRef target, IEventListener<EntityRef> listener)
    {
        World.Dispatcher.Listen(target, listener);
        OnUnlisten += () => World.Dispatcher.Unlisten(target, listener);
    }
    
    public virtual void OnInitialize(World world)
    {
        World = world;
    }

    public virtual void OnUninitialize(World world)
    {
        OnUnlisten?.Invoke();
        OnUnlisten = null;
    }
}

public abstract class ViewBase<TTypeUnion> : ViewBase
    where TTypeUnion : ITypeUnion, new()
{
    [AllowNull]
    protected IReactiveEntityQuery Query { get; private set; }

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);

        Query = world.Query<TTypeUnion>();
        Query.OnEntityHostAdded += OnEntityHostAdded;

        foreach (var host in Query.Hosts) {
            OnEntityHostAdded(host);
        }
    }

    private void OnEntityHostAdded(IReactiveEntityHost host)
    {
        host.OnEntityCreated += OnEntityAdded;
        host.OnEntityReleased += OnEntityRemoved;

        foreach (var entity in host) {
            OnEntityAdded(entity);
        }
    }

    public override void OnUninitialize(World world)
    {
        base.OnInitialize(world);

        foreach (var host in Query.Hosts) {
            host.OnEntityCreated -= OnEntityAdded;
            host.OnEntityReleased -= OnEntityRemoved;
        }

        Query.Dispose();
        Query = null;
    }

    protected abstract void OnEntityAdded(in EntityRef entity);
    protected abstract void OnEntityRemoved(in EntityRef entity);
}