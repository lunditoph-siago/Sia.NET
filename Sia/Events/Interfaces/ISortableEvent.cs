namespace Sia;

public interface ISortableEvent : IEvent
{
    int Priority { get; }
}