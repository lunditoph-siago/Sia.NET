namespace Sia;

using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

public sealed class PoolStorage<T> : IStorage<T>, IDisposable
{
    public int Capacity { get; }
    public int Count { get; private set; }

    private MemoryOwner<T> _memory;
    private IntPtr _initialPtr;
    private int _lastIndex;

    private SparseSet<int> _allocated;
    private SparseSet<int> _released;

    private bool _disposed;

    private const int IndexPageSize = 1024;

    public PoolStorage()
        : this(512)
    {
    }

    public unsafe PoolStorage(int capacity)
    {
        Capacity = capacity;

        _memory = MemoryOwner<T>.Allocate(Capacity);
        _initialPtr = (IntPtr)Unsafe.AsPointer(ref _memory.Span[0]);

        if (capacity <= IndexPageSize) {
            _allocated = new(1, IndexPageSize);
            _released = new(1, IndexPageSize);
        }
        else {
            int pageCount = capacity / IndexPageSize + capacity % IndexPageSize;
            _allocated = new(pageCount, IndexPageSize);
            _released = new(pageCount, IndexPageSize);
        }
    }

    public unsafe IntPtr Allocate()
    {
        if (Count == Capacity) {
            throw new IndexOutOfRangeException("Pool storage is full");
        }

        IntPtr ptr;
        int releasedCount = _released.Count;

        if (releasedCount > 0) {
            int index = _released.AsKeySpan()[releasedCount - 1];
            _released.Remove(index);
            _allocated.Add(index, index);
            ptr = _initialPtr + index;
        }
        else {
            while (true) {
                if (_allocated.Add(_lastIndex, _lastIndex)) {
                    break;
                }
                _lastIndex = (_lastIndex + 1) % Capacity;
            }
            ptr = _initialPtr + _lastIndex;
        }

        Count++;
        return ptr;
    }

    public void Release(IntPtr ptr)
    {
        int index = (int)(ptr - _initialPtr);
        if (ptr < _initialPtr || !_allocated.Remove(index)) {
            throw new ArgumentException("Entity was not allocated from this storage");
        }

        if (_lastIndex == index) {
            _lastIndex--;
        }
        else {
            _released.Add(index, index);
        }
        Count--;
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _memory.Dispose();
    }
}