namespace Sia;

using System.Diagnostics.CodeAnalysis;

public abstract class ViewBase<TTypeUnion> : IAddon
    where TTypeUnion : ITypeUnion, new()
{
    protected delegate void ForeverListener<UEvent>(in EntityRef target, in UEvent e)
        where UEvent : IEvent;

    [AllowNull]
    public World World { get; private set; }

    [AllowNull]
    protected World.EntityQuery Query { get; private set; }

    private event Action? OnUnlisten;

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

    public virtual void OnInitialize(World world)
    {
        World = world;
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

        host.IterateAllocated((this, host),
            static (in (ViewBase<TTypeUnion> View, IReactiveEntityHost Host) data, long pointer) =>
                data.View.OnEntityAdded(new(pointer, data.Host)));
    }

    public virtual void OnUninitialize(World world)
    {
        OnUnlisten?.Invoke();
        OnUnlisten = null;

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