using System.Diagnostics;

namespace Sia;

using System.Runtime.CompilerServices;

public struct ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public readonly int Capacity => int.MaxValue;

    private T[] _values = new T[CalculateArraySize(initialCapacity)];

    public ref T this[int index] {
        get {
            CheckElementReadAccess(index);
            return ref index >= _values.Length ? ref Unsafe.NullRef<T>() : ref _values[index];
        }
    }

    public ArrayBuffer() : this(0) {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateArraySize(int requiredCapacity)
    {
        int size = 8;
        while (size < requiredCapacity) { size *= 2; }
        return size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T CreateRef(int index)
    {
        if (index >= _values.Length) {
            var newArr = new T[CalculateArraySize(index + 1)];
            _values.CopyTo(newArr.AsSpan());
            _values = newArr;
        }
        return ref _values[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Release(int index)
    {
        if (index >= _values.Length) {
            return false;
        }
        _values[index] = default!;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsAllocated(int index)
        => index < _values.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Clear() => Array.Clear(_values);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Dispose() {}

    [Conditional("ENABLE_COLLECTIONS_CHECKS")]
    private void CheckElementReadAccess(int index)
    {
        if (index < 0 || index >= _values.Length)
            throw new IndexOutOfRangeException($"Index {(object)index} is out of range of '{(object)_values.Length}' Length.");
    }
}