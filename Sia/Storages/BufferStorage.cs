namespace Sia;

public sealed class BufferStorage<T> : IStorage<T>
    where T : struct
{
    public int Capacity { get; }
    public int Count { get; private set; }
    public bool IsManaged => true;

    public int PageSize { get; }

    private int _lastIndex;

    private readonly SparseSet<T> _memory;
    private readonly SparseSet<int> _released;

    public BufferStorage(int capacity, int pageSize = 256)
    {
        Capacity = capacity;
        PageSize = pageSize;

        if (capacity <= PageSize) {
            _memory = new(1, capacity);
            _released = new(1, capacity);
        }
        else {
            int pageCount = capacity / PageSize + (capacity % PageSize != 0 ? 1 : 0);
            _memory = new(pageCount, PageSize);
            _released = new(pageCount, PageSize);
        }
    }

    public Pointer<T> Allocate()
    {
        if (Count == Capacity) {
            throw new IndexOutOfRangeException("Storage is full");
        }

        int index;
        int releasedCount = _released.Count;

        if (releasedCount > 0) {
            index = _released.AsKeySpan()[releasedCount - 1];
            _released.Remove(index);
            _memory.GetOrAddValueRef(index, out bool _);
        }
        else {
            while (true) {
                _memory.GetOrAddValueRef(_lastIndex, out bool exists);
                if (!exists) { break; }
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
        if (!_memory.Remove(index)) {
            throw new ArgumentException("Invalid pointer");
        }
        if (_lastIndex == index) {
            _lastIndex--;
        }
        else {
            _released.Add(index, index);
        }
        Count--;
    }

    public ref T UnsafeGetRef(long rawPointer)
        => ref _memory.GetValueRefOrNullRef((int)rawPointer);
}