namespace Sia;

using System.Collections.Concurrent;

public abstract class PooledEvent<TEvent> : IEvent, IDisposable
    where TEvent : PooledEvent<TEvent>, new()
{
    private readonly static ConcurrentStack<TEvent> s_pool = new();

    private int _disposed;

    protected static TEvent CreateRaw()
    {
        if (s_pool.TryPop(out var e)) {
            Interlocked.Exchange(ref e._disposed, 0);
            return e;
        }
        return new();
    }

#pragma warning disable CA1816
    public virtual void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) {
            return;
        }
        s_pool?.Push((TEvent)this);
    }
#pragma warning restore CA1816
}