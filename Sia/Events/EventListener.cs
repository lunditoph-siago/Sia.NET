namespace Sia;

public class EventListener<TTarget> : IDisposable
    where TTarget : notnull
{
    public Dispatcher<TTarget> Dispatcher { get; }
    public Dispatcher<TTarget>.Listener Listener { get; }

    private bool _disposed;

    public EventListener(Dispatcher<TTarget> dispatcher, Dispatcher<TTarget>.Listener listener)
    {
        Dispatcher = dispatcher;
        Listener = listener;
        dispatcher.Listen(listener);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) { return; }
        Dispatcher.Unlisten(Listener);
        _disposed = true;
    }

    ~EventListener()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public class EventListener : EventListener<EntityRef>
{
    public EventListener(Dispatcher<EntityRef> dispatcher, Dispatcher<EntityRef>.Listener listener)
        : base(dispatcher, listener)
    {
    }
}