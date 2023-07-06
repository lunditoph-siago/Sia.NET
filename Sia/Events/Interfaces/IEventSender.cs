namespace Sia;

public interface IEventSender<TEvent, TTarget>
    where TEvent : IEvent
{
    void Send(in TTarget target, TEvent e);
}

public interface IEventSender<TEvent> : IEventSender<TEvent, EntityRef>
    where TEvent : IEvent
{
}