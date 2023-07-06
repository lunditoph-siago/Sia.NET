namespace Sia;

public abstract class SingleValuePooledEvent<TEvent, T> : PooledEvent<TEvent>
    where TEvent : IEvent, new()
{
    public T? Value { get; set; }

    public static TEvent Create(T value)
    {
        var e = CreateRaw();
        (e as SingleValuePooledEvent<TEvent, T>)!.Value = value;
        return e;
    }
}