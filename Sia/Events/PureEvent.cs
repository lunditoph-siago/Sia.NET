namespace Sia;

public sealed class PureEvent<TEvent> : IEvent
    where TEvent : IEvent
{
    public static readonly PureEvent<TEvent> Instance = new();

    private PureEvent() { }
}