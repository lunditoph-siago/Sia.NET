namespace Sia;

public abstract class SingletonEvent<TEvent> : IEvent
    where TEvent : IEvent, new()
{
    public static TEvent Instance { get; } = new();

    protected SingletonEvent() {}

    public void Dispose() {}
}