namespace Sia;

public abstract class PooledEvent<TEvent> : IEvent
    where TEvent : PooledEvent<TEvent>, new()
{
    [ThreadStatic]
    private static Stack<TEvent>? s_pool;

    public bool IsDisposed { get; private set; }

    protected static TEvent CreateRaw()
    {
        s_pool ??= new();

        if (s_pool.TryPop(out var e)) {
            e.IsDisposed = false;
            return e;
        }
        return new();
    }

    public virtual void Dispose()
    {
        if (IsDisposed) {
            return;
        }
        s_pool?.Push((TEvent)this);
        IsDisposed = true;
    }
}