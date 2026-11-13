using System.Diagnostics.CodeAnalysis;

namespace Sia.Reactors;

public abstract class ReactorBase : IAddon
{
    protected delegate void ForeverListener<UEvent>(Entity target, in UEvent e)
        where UEvent : IEvent;

    private event Action? OnUnlisten;

    [AllowNull]
    public World World { get; private set; }

    protected void Listen(IEventListener<Entity> listener)
    {
        var dispatcher = World.Dispatcher;
        dispatcher.Listen(listener);
        OnUnlisten += () => dispatcher.RetireListener(listener);
    }

    protected void Listen<UEvent>(WorldDispatcher.Listener<UEvent> listener)
        where UEvent : IEvent
    {
        var dispatcher = World.Dispatcher;
        dispatcher.Listen(listener);
        OnUnlisten += () => dispatcher.RetireListener(listener);
    }

    protected void Listen<UEvent>(ForeverListener<UEvent> listener)
        where UEvent : IEvent
    {
        bool Listener(Entity entity, in UEvent command)
        {
            listener(entity, command);
            return false;
        }
        var dispatcher = World.Dispatcher;
        dispatcher.Listen<UEvent>(Listener);
        OnUnlisten += () => dispatcher.RetireListener<UEvent>(Listener);
    }

    protected void Listen(Entity target, IEventListener<Entity> listener)
    {
        var dispatcher = World.Dispatcher;
        dispatcher.Listen(target, listener);
        OnUnlisten += () => dispatcher.RetireListener(target, listener);
    }

    public virtual void OnInitialize(World world)
    {
        World = world;
    }

    public virtual void OnUninitialize(World world)
    {
        var cleanup = OnUnlisten;
        OnUnlisten = null;
        if (cleanup is null) {
            return;
        }
        var result = Outcome<Exception>.Success;
        foreach (Action unlisten in cleanup.GetInvocationList()) {
            result = result.Attempt(unlisten);
        }
        result.ThrowIfFailed();
    }
}

public abstract class ReactorBase<TTypeUnion> : ReactorBase
    where TTypeUnion : ITypeUnion, new()
{
    protected IEntityMatcher Matcher { get; } = Matchers.From<TTypeUnion>();

    [AllowNull]
    protected IReactiveEntityQuery Query => _subscription?.Query!;

    [AllowNull]
    private QuerySubscription _subscription;

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);
        _subscription = new QuerySubscription(world.Query(Matcher), OnEntityAdded, OnEntityRemoved);
    }

    public override void OnUninitialize(World world)
    {
        var subscription = _subscription;
        _subscription = null;
        var result = Outcome<Exception>.Success.Attempt(() => base.OnUninitialize(world));
        if (subscription is not null) {
            result = result.Attempt(subscription.Dispose);
        }
        result.ThrowIfFailed();
    }

    protected abstract void OnEntityAdded(Entity entity);
    protected abstract void OnEntityRemoved(Entity entity);
}
