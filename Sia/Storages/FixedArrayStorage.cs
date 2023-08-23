namespace Sia;

using System.Buffers;
using System.Runtime.CompilerServices;

public sealed class FixedArrayStorage<T> : IStorage<T>
    where T : struct
{
    public int Capacity { get; }
    public int Count { get; private set; }
    public int PointerValidBits => 32;
    public bool IsManaged => true;

    private int _lastIndex;

    private T[] _arr;
    private int[] _released;
    private int _releasedCount;

    public FixedArrayStorage(int capacity)
    {
        if (capacity <= 0) {
            throw new ArgumentException("Invalid capacity");
        }
        Capacity = capacity;

        _arr = ArrayPool<T>.Shared.Rent(capacity);
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
        => ref _arr[rawPointer];
    
    public void Dispose()
    {
        if (_arr == null) {
            return;
        }

        ArrayPool<T>.Shared.Return(_arr);
        ArrayPool<int>.Shared.Return(_released);

        _arr = null!;
        _released = null!;

        GC.SuppressFinalize(this);
    }
}