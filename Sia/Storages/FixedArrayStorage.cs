namespace Sia;

using System.Buffers;
using System.Runtime.CompilerServices;

public sealed class FixedArrayStorage<T> : IStorage<T>
    where T : struct
{
    private struct Entry
    {
        public bool IsAllocated;
        public T Value;
    }

    public int Capacity { get; }
    public int Count { get; private set; }
    public int PointerValidBits => 32;
    public bool IsManaged => true;

    private int _lastIndex;

    private Entry[] _arr;
    private int[] _released;
    private int _releasedCount;

    public FixedArrayStorage(int capacity)
    {
        if (capacity <= 0) {
            throw new ArgumentException("Invalid capacity");
        }
        Capacity = capacity;

        _arr = ArrayPool<Entry>.Shared.Rent(capacity);
        _released = ArrayPool<int>.Shared.Rent(capacity);
    }

    ~FixedArrayStorage()
    {
        Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pointer<T> Allocate()
    {
        if (Count == Capacity) {
            throw new IndexOutOfRangeException("Storage is full");
        }

        int index;
        int nextIndex = _releasedCount - 1;

        if (nextIndex != -1) {
            index = _released[nextIndex];
            _releasedCount--;
        }
        else {
            index = ++_lastIndex;
        }

        Count++;
        _arr[index].IsAllocated = true;
        return new(index, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(long rawPointer)
    {
        int index = (int)rawPointer;
        _arr[index] = default;
        _released[_releasedCount++] = index;
        Count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(long rawPointer)
        => ref _arr[rawPointer].Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(Action<long> func)
    {
        var count = _arr.Length;
        for (int i = 0; i != count; ++i) {
            if (_arr[i].IsAllocated) {
                func(i);
            }
        }
    }
    
    public void Dispose()
    {
        if (_arr == null) {
            return;
        }

        ArrayPool<Entry>.Shared.Return(_arr);
        ArrayPool<int>.Shared.Return(_released);

        _arr = null!;
        _released = null!;

        GC.SuppressFinalize(this);
    }
}