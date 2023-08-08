namespace Sia;

public abstract class SingleValuePooledEvent<TEvent, TValue> : PooledEvent<TEvent>
    where TEvent : SingleValuePooledEvent<TEvent, TValue>, new()
{
    public TValue? Value { get; set; }

    public static TEvent Create(TValue value)
    {
        var e = CreateRaw();
        (e as SingleValuePooledEvent<TEvent, TValue>)!.Value = value;
        return e;
    }
}