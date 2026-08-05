namespace Sia;

public interface IHigherOrderEvent : IEvent
{
    public Type InnerEventType { get; }
}