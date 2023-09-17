namespace Sia;

public abstract class SingletonEvent<TEvent> : IEvent
    where TEvent : SingletonEvent<TEvent>, new()
{
    public static TEvent Instance { get; } = new();

    protected SingletonEvent() {}
}