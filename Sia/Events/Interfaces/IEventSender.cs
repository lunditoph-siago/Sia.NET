namespace Sia;

public interface IEventSender<TTarget, TEvent>
    where TEvent : IEvent
{
    void Send<UEvent>(in TTarget target, in UEvent e)
        where UEvent : TEvent;
}

public interface IEventSender<TEvent> : IEventSender<EntityRef, TEvent>
    where TEvent : IEvent
{
}
public interface IEventSender : IEventSender<IEvent>
{
}