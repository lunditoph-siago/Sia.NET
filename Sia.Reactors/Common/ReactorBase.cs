namespace Sia.Reactors;

using System.Diagnostics.CodeAnalysis;

public abstract class ReactorBase : IAddon
{
    protected delegate void ForeverListener<UEvent>(Entity target, in UEvent e)
        where UEvent : IEvent;

    private event Action? OnUnlisten;

    [AllowNull]
    public World World { get; private set; }

    protected void Listen(IEventListener<Entity> listener)
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
        bool Listener(Entity entity, in UEvent command)
        {
            listener(entity, command);
            return false;
        }
        World.Dispatcher.Listen<UEvent>(Listener);
        OnUnlisten += () => World.Dispatcher.Unlisten<UEvent>(Listener);
    }

    protected void Listen(Entity target, IEventListener<Entity> listener)
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

public abstract class ReactorBase<TTypeUnion> : ReactorBase
    where TTypeUnion : ITypeUnion, new()
{
    protected IEntityMatcher Matcher { get; } = Matchers.From<TTypeUnion>();

    [AllowNull]
    protected IReactiveEntityQuery Query { get; private set; }

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);

        Query = world.Query(Matcher);
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

    protected abstract void OnEntityAdded(Entity entity);
    protected abstract void OnEntityRemoved(Entity entity);
}