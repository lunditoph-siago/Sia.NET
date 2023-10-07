namespace Sia;

public static class EventQueueExtensions
{
    public static EventQueue<TTarget, TEvent> CreateEventQueue<TTarget, TEvent>(
        this IEventSender<TTarget, TEvent> receiver)
        where TEvent : IEvent
        => new(receiver);
}