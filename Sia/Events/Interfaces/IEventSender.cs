namespace Sia;

public interface IEventSender<TTarget, in TEvent>
    where TEvent : IEvent
{
    void Send<UEvent>(in TTarget target, in UEvent e)
        where UEvent : TEvent;
}