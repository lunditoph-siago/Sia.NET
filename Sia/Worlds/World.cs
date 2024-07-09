namespace Sia;

using System.Runtime.CompilerServices;

public sealed partial class World : IReactiveEntityQuery, IEventSender
{
    public static World Current => Context.Get<World>();

    public event Action<World>? OnDisposed;

    public int Count { get; internal set; }
    public bool IsDisposed { get; private set; }
    public int Version { get; internal set; }

    public WorldDispatcher Dispatcher { get; }

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
        IsDisposed = true;

        ClearHosts();
        ClearAddons();

        OnDisposed?.Invoke(this);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send<TEvent>(Entity target, in TEvent e)
        where TEvent : IEvent
        => Dispatcher.Send(target, e);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute<TCommand>(Entity target, in TCommand command)
        where TCommand : ICommand
    {
        command.Execute(this, target);
        Dispatcher.Send(target, command);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute<TComponent, TCommand>(
        Entity target, ref TComponent component, in TCommand command)
        where TCommand : ICommand<TComponent>
    {
        command.Execute(this, target, ref component);
        Dispatcher.Send(target, command);
    }
}