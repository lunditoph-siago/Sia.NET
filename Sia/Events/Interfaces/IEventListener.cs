namespace Sia;

public interface IEventListener<TTarget>
{
    bool OnEvent<TEvent>(in TTarget target, in TEvent e)
        where TEvent : IEvent;
}