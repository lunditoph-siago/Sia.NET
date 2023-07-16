namespace Sia;

using CommunityToolkit.HighPerformance.Buffers;

public sealed class BufferStorage<T> : IStorage<T>, IDisposable
    where T : struct
{
    public int Capacity { get; }
    public int Count { get; private set; }
    public bool IsManaged => true;

    private readonly MemoryOwner<T> _memory;
    private int _lastIndex;

    private readonly SparseSet<int> _allocated;
    private readonly SparseSet<int> _released;

    private bool _disposed;

    private const int IndexPageSize = 1024;

    public unsafe BufferStorage(int capacity)
    {
        Capacity = capacity;
        _memory = MemoryOwner<T>.Allocate(Capacity, AllocationMode.Clear);

        if (capacity <= IndexPageSize) {
            _allocated = new(1, capacity);
            _released = new(1, capacity);
        }
        else {
            int pageCount = capacity / IndexPageSize + capacity % IndexPageSize;
            _allocated = new(pageCount, IndexPageSize);
            _released = new(pageCount, IndexPageSize);
        }
    }

    public unsafe Pointer<T> Allocate()
    {
        if (Count == Capacity) {
            throw new IndexOutOfRangeException("Pool storage is full");
        }

        int index;
        int releasedCount = _released.Count;

        if (releasedCount > 0) {
            index = _released.AsKeySpan()[releasedCount - 1];
            _released.Remove(index);
            _allocated.Add(index, index);
        }
        else {
            while (true) {
                if (_allocated.Add(_lastIndex, _lastIndex)) {
                    break;
                }
                _lastIndex = (_lastIndex + 1) % Capacity;
            }
            index = _lastIndex;
        }

        Count++;
        return new(index, this);
    }

    public void UnsafeRelease(long rawPointer)
    {
        int index = (int)rawPointer;
        if (!_allocated.Remove(index)) {
            throw new ArgumentException("Invalid pointer");
        }

        if (_lastIndex == index) {
            _lastIndex--;
        }
        else {
            _released.Add(index, index);
        }

        _memory.Span[index] = default;
        Count--;
    }

    public ref T UnsafeGetRef(long rawPointer)
        => ref _memory.Span[(int)rawPointer];

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _memory.Dispose();
    }
}