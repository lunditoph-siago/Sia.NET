namespace Sia;

using System.Collections;
using CommunityToolkit.HighPerformance.Buffers;

public class SizedHistory<TTarget, TEvent> : IHistory<TTarget, TEvent>
    where TTarget : notnull
    where TEvent : IEvent
{
    public struct Enumerator : IEnumerator<EventPair<TTarget, TEvent>>
    {
        public readonly EventPair<TTarget, TEvent> Current {
            get {
                if (_index == -1) {
                    throw new InvalidOperationException("Use MoveNext to move the enumerator to the first element");
                }
                return _history._events.Span[_index];
            }
        }

        readonly object IEnumerator.Current => _history._events.Span[_index]!;

        private readonly SizedHistory<TTarget, TEvent> _history;

        private uint _version;
        private int _index = -1;
        private int _accum = 0;

        internal Enumerator(SizedHistory<TTarget, TEvent> history)
        {
            _history = history;
            _version = _history._version;
        }

        public readonly void Dispose() {}

        public bool MoveNext()
        {
            if (_version != _history._version) {
                throw new InvalidOperationException("History was modified during enumeration");
            }

            int count = _history.Count;
            int capacity = _history.Capacity;

            if (_accum >= count) {
                return false;
            }

            if (_index == -1) {
                _index = (_history._lastIndex + capacity) % capacity;
                _accum++;
                return true;
            }

            _index = (_index + 1) % capacity;
            _accum++;
            return true;
        }

        public void Reset()
        {
            _version = _history._version;
            _index = -1;
            _accum = 0;
        }
    }

    public EventPair<TTarget, TEvent>? Last { get; private set; }

    public int Capacity {
        get => _capacity;
        set {
            if (value == _events.Length) {
                return;
            }

            var newMem = MemoryOwner<EventPair<TTarget, TEvent>>.Allocate(value);
            _events.Span[0..Math.Min(_events.Length, value)].CopyTo(newMem.Span);
            _events.Dispose();
            _events = newMem;

            _capacity = value;
            _version++;
        }
    }

    public EventPair<TTarget, TEvent> this[int index] {
        get {
            if (index < 0 || index >= Count) {
                throw new IndexOutOfRangeException("Index out of range");
            }
            return _events.Span[(_lastIndex + Capacity + index) % Capacity];
        }
    }

    public int Count { get; private set; }

    private uint _version;
    private int _capacity;
    private int _lastIndex;

    public MemoryOwner<EventPair<TTarget, TEvent>> _events;

    private bool _disposed;

    public SizedHistory(int capacity)
    {
        if (capacity <= 0) {
            throw new ArgumentException("Capacity must be bigger than 0");
        }
        _capacity = capacity;
        _events = MemoryOwner<EventPair<TTarget, TEvent>>.Allocate(capacity);
    }

    public void Record(in TTarget target, in TEvent e)
    {
        ref var pair = ref _events.Span[_lastIndex];
        pair = new EventPair<TTarget, TEvent>(target, e);
        Last = pair;
        _lastIndex = (_lastIndex + 1) % Capacity;

        if (Count < Capacity) {
            Count++;
        }
        _version++;
    }

    public bool Contains(in EventPair<TTarget, TEvent> pair)
    {
        var span = _events.Span;
        var adder = _lastIndex + Capacity;

        for (int i = 0; i < Count; ++i) {
            if (span[(i + adder) % Capacity] == pair) {
                return true;
            }
        }
        return false;
    }

    public void CopyTo(EventPair<TTarget, TEvent>[] array, int arrayIndex)
    {
        var span = _events.Span;
        var adder = _lastIndex + Capacity;

        for (int i = 0; i < Count; ++i) {
            array[arrayIndex + i] = span[(i + adder) % Capacity];
        }
    }

    public int IndexOf(EventPair<TTarget, TEvent> item)
    {
        var span = _events.Span;
        var adder = _lastIndex + Capacity;

        for (int i = 0; i < Count; ++i) {
            if (span[(i + adder) % Capacity] == item) {
                return i;
            }
        }
        return -1;
    }

    public EventPair<TTarget, TEvent>[] ToArray()
    {
        var result = new EventPair<TTarget, TEvent>[Count];
        var span = _events.Span;
        var adder = _lastIndex + Capacity;

        for (int i = 0; i < Count; ++i) {
            result[i] = span[(i + adder) % Capacity];
        }
        return result;
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<EventPair<TTarget, TEvent>> IEnumerable<EventPair<TTarget, TEvent>>.GetEnumerator()
        => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    private void Dispose(bool disposing)
    {
        if (_disposed) { return; }
        _disposed = true;

        if (disposing) {
            _events.Dispose();
        }
        _version++;
    }

    ~SizedHistory()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}