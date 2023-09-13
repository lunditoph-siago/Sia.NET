namespace Sia;

public interface IEventSender<TTarget, TEvent>
    where TEvent : IEvent
{
    void Send<UEvent>(in TTarget target, in UEvent e)
        where UEvent : TEvent;
}