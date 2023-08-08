namespace Sia;

public interface IEventSender<in TEvent, TTarget>
    where TEvent : IEvent
{
    void Send(in TTarget target, TEvent e);
}

public interface IEventSender<in TEvent> : IEventSender<TEvent, EntityRef>
    where TEvent : IEvent
{
}