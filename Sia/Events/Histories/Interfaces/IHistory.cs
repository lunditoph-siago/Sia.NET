namespace Sia;

public interface IHistory<TTarget, TEvent> : IReadOnlyList<EventPair<TTarget, TEvent>>, IDisposable
    where TTarget : notnull
    where TEvent : IEvent
{
    Dispatcher<TTarget, TEvent> Dispatcher { get; }
    EventPair<TTarget, TEvent>? Last { get; }
}