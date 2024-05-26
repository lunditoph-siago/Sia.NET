namespace Sia;

public interface IEventListener<TTarget>
{
    bool OnEvent<TEvent>(TTarget target, in TEvent e)
        where TEvent : IEvent;
}