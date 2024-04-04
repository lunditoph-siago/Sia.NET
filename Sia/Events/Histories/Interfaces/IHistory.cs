namespace Sia;

public interface IHistory<TTarget, TEvent> : IReadOnlyList<EventPair<TTarget, TEvent>>, IDisposable
    where TTarget : notnull
    where TEvent : IEvent
{
    EventPair<TTarget, TEvent>? Last { get; }
    void Record(in TTarget target, in TEvent e);
}