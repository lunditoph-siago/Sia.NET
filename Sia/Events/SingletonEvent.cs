namespace Sia;

public abstract class SingletonEvent<TEvent> : IEvent
    where TEvent : new()
{
    public static TEvent Instance { get; } = new();

    protected SingletonEvent() { }
}