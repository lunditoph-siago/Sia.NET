namespace Sia;

public interface IHigherOrderEvent
{
    public Type InnerEventType { get; }
}