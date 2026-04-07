namespace Sia;

public interface IEventListener<in TTarget>
{
    bool OnEvent<TEvent>(TTarget target, in TEvent e)
        where TEvent : IEvent;
}