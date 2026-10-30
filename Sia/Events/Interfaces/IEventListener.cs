namespace Sia;

public interface IEventListener<in TTarget>
{
    bool OnEvent<TEvent>(TTarget target, in TEvent e)
        where TEvent : IEvent;
}

public interface IEventSink<in TTarget>;

public interface IEventSink<in TTarget, TEvent> : IEventSink<TTarget> where TEvent : IEvent
{
    void OnEvent(TTarget target, in TEvent e);
}
