namespace Sia;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

public class BlockingQueue<T>
{
    public bool IsCompleted { get; private set; }

    private readonly ConcurrentQueue<T> _queue = new();
    private readonly AutoResetEvent _autoResetEvent = new(false);

    public void Enqueue(T item)
    {
        if (IsCompleted) {
            throw new InvalidOperationException("BlockingQueue has been completed");
        }
        _queue.Enqueue(item);
        _autoResetEvent.Set();
    }

    public bool TryPeek([MaybeNullWhen(false)] out T result)
        => _queue.TryPeek(out result);

    public bool Dequeue([MaybeNullWhen(false)] out T item)
    {
        if (IsCompleted) {
            item = default;
            return false;
        }
        while (!_queue.TryDequeue(out item)) {
            _autoResetEvent.WaitOne();
            if (IsCompleted) {
                item = default;
                return false;
            }
        }
        return true;
    }

    public void Complete()
    {
        if (IsCompleted) {
            return;
        }
        IsCompleted = true;
        _autoResetEvent.Set();
    }
}
