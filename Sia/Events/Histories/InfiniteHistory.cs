namespace Sia;

using System.Collections;

public class InfiniteHistory<TTarget, TEvent> : IHistory<TTarget, TEvent>
    where TTarget : notnull
    where TEvent : IEvent
{
    public EventPair<TTarget, TEvent>? Last { get; private set; }

    public EventPair<TTarget, TEvent> this[int index] => _list[index];
    public int Count => _list.Count;

    private readonly List<EventPair<TTarget, TEvent>> _list = [];

    private bool _disposed;

    public void Record(in TTarget target, in TEvent e)
    {
        var pair = new EventPair<TTarget, TEvent>(target, e);
        Last = pair;
        _list.Add(pair);
    }

    public bool Contains(EventPair<TTarget, TEvent> item)
        => _list.Contains(item);

    public void CopyTo(EventPair<TTarget, TEvent>[] array, int arrayIndex)
        => _list.CopyTo(array, arrayIndex);

    public int IndexOf(EventPair<TTarget, TEvent> item)
        => _list.IndexOf(item);

    public List<EventPair<TTarget, TEvent>>.Enumerator GetEnumerator()
        => _list.GetEnumerator();

    IEnumerator<EventPair<TTarget, TEvent>> IEnumerable<EventPair<TTarget, TEvent>>.GetEnumerator()
        => ((IEnumerable<EventPair<TTarget, TEvent>>)_list).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)_list).GetEnumerator();
    
    private void Dispose(bool disposing)
    {
        if (_disposed) { return; }
        _disposed = true;

        if (disposing) {
            _list.Clear();
        }
    }

    ~InfiniteHistory()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}