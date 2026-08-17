namespace Sia;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

public class BlockingQueue<T>
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<T> _queue = new();
    private readonly AutoResetEvent _itemEvent = new(false);
    private readonly ManualResetEventSlim _completedEvent = new(false);
    private readonly WaitHandle[] _waitHandles;
    private volatile bool _isCompleted;

    public BlockingQueue()
    {
        _waitHandles = [_itemEvent, _completedEvent.WaitHandle];
    }

    public bool IsCompleted => _isCompleted;

    public void Enqueue(T item)
    {
        lock (_gate) {
            if (_isCompleted) {
                throw new InvalidOperationException("BlockingQueue has been completed");
            }
            _queue.Enqueue(item);
        }
        _itemEvent.Set();
    }

    public bool TryPeek([MaybeNullWhen(false)] out T result)
        => _queue.TryPeek(out result);

    public bool TryDequeue([MaybeNullWhen(false)] out T result)
        => _queue.TryDequeue(out result);

    public bool Dequeue([MaybeNullWhen(false)] out T item)
    {
        while (true) {
            if (_queue.TryDequeue(out item)) {
                return true;
            }
            if (_isCompleted) {
                item = default;
                return false;
            }
#if BROWSER
            if (WaitHandle.WaitAny(_waitHandles, 1500) == WaitHandle.WaitTimeout
                && _queue.IsEmpty) {
                item = default;
                return false;
            }
#else
            WaitHandle.WaitAny(_waitHandles);
#endif
        }
    }

    public void Complete()
    {
        lock (_gate) {
            if (_isCompleted) {
                return;
            }
            _isCompleted = true;
        }
        _completedEvent.Set();
    }
}
