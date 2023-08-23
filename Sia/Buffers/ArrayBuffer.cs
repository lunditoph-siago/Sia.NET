namespace Sia;

using System.Runtime.CompilerServices;

public sealed class ArrayBuffer<T> : IBuffer<T>
{
    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _arr.Length;
    }

    public int Count { get; set; }

    private Entry[] _arr;

    private struct Entry
    {
        public bool IsAllocated;
        public T Value;
    }

    public ArrayBuffer(int capacity)
    {
        _arr = new Entry[capacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetOrAddValueRef(int index, out bool exists)
    {
        ref var entry = ref _arr[index];
        exists = entry.IsAllocated;
        return ref entry.Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetValueRefOrNullRef(int index)
    {
        ref var entry = ref _arr[index];
        if (entry.IsAllocated) {
            return ref Unsafe.NullRef<T>();
        }
        return ref entry.Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int index)
    {
        ref var entry = ref _arr[index];
        if (!entry.IsAllocated) {
            return false;
        }
        entry.IsAllocated = false;
        entry.Value = default!;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        _arr = null!;
    }
}