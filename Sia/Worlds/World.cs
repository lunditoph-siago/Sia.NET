namespace Sia;

using System.Runtime.CompilerServices;

public sealed partial class World : IReactiveEntityQuery, IEventSender
{
    public event Action<World>? OnDisposed;

    public int Count { get; internal set; }
    public bool IsDisposed { get; private set; }
    public int Version { get; internal set; }

    public WorldDispatcher Dispatcher { get; }
    public IWorldCommandQueue Commands { get; } = new WorldCommandQueue();

    internal EntityStatePool EntityStates { get; } = new();

    private WorldActor? _actor;

    public World()
    {
        Dispatcher = new WorldDispatcher(this);
    }

    ~World()
    {
        Dispose(false);
    }

    private void Dispose(bool disposing)
    {
        if (IsDisposed) { return; }

        if (disposing) {
            ThrowIfActorIncomplete();
        }
        else if (_actor is { IsCompleted: false }) {
            return;
        }

        IsDisposed = true;

        Outcome<Exception>.Success
            .Attempt(ClearHosts)
            .Attempt(EntityStates.Retire)
            .Attempt(ClearAddons)
            .Attempt(() => OnDisposed?.Invoke(this))
            .ThrowIfFailed();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send<TEvent>(Entity target, in TEvent e)
        where TEvent : IEvent
    {
        EnsureOwnerThread();
        ThrowIfForeignEntity(target);
        Dispatcher.Send(target, e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute<TCommand>(Entity target, in TCommand command)
        where TCommand : ICommand
    {
        EnsureOwnerThread();
        ThrowIfForeignEntity(target);
        command.Execute(this, target);
        Dispatcher.Send(target, command);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute<TComponent, TCommand>(
        Entity target, ref TComponent component, in TCommand command)
        where TCommand : ICommand<TComponent>
    {
        EnsureOwnerThread();
        ThrowIfForeignEntity(target);
        command.Execute(this, target, ref component);
        Dispatcher.Send(target, command);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfForeignEntity(Entity target)
    {
        var owner = EntityWorldOwner.TryGet(target);
        if (owner is not null && !ReferenceEquals(owner, this)) {
            throw new ArgumentException(
                "The entity belongs to a different world.", nameof(target));
        }
    }

    private void ThrowIfActorIncomplete()
    {
        if (_actor is { IsCompleted: false }) {
            throw new InvalidOperationException(
                "The world is bound to a WorldActor that has not completed. " +
                "Call WorldActor.CompleteAndDispose(...) instead of World.Dispose() directly.");
        }
    }

    internal void BindActor(WorldActor actor)
    {
        if (Interlocked.CompareExchange(ref _actor, actor, null) is { } existing
            && !ReferenceEquals(existing, actor)) {
            throw new InvalidOperationException(
                "The world is already bound to a different actor.");
        }
    }

    public WorldActor? Actor => _actor;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Post(IWorldMessage message)
        => (Actor ?? throw new InvalidOperationException(
                "The world is not bound to an actor."))
            .Post(message);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Post(in CommandRequest request)
        => (Actor ?? throw new InvalidOperationException(
                "The world is not bound to an actor."))
            .Post(request);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Post<TEvent>(Entity target, in TEvent e)
        where TEvent : IEvent
        => (Actor ?? throw new InvalidOperationException(
                "The world is not bound to an actor."))
            .Post(target, e);

    public void EnsureOwnerThread()
    {
        var actor = _actor;
        if (actor != null && !actor.IsOwnerThread) {
            throw new InvalidOperationException(
                "World operations must run on the actor owner thread. " +
                "Post a message to the WorldActor instead.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldEntity Spawn()
        => new(this, this.Create());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldEntity Spawn<TEntity>(in TEntity initial)
        where TEntity : struct, IHList
        => new(this, this.Create(initial));
}
