namespace Sia;

using System.Collections.Concurrent;

public abstract class PooledEvent<TEvent> : IEvent
    where TEvent : IEvent, new()
{
    private static readonly ConcurrentStack<IEvent> s_pool = new();

    public bool IsDisposed { get; private set; }

    protected static TEvent CreateRaw()
    {
        if (s_pool.TryPop(out var e)) {
            ((PooledEvent<TEvent>)e).IsDisposed = false;
            return (TEvent)e;
        }
        return new TEvent();
    }

    public virtual void Dispose()
    {
        if (IsDisposed) {
            return;
        }
        s_pool.Push(this);
        IsDisposed = true;
    }
}