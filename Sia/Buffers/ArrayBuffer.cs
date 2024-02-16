namespace Sia;

using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public struct ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public readonly int Capacity => int.MaxValue;

    private T[] _values = new T[CalculateArraySize(initialCapacity)];

    public readonly ref T this[int index] => ref GetRef(index);

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
    public readonly ref T GetRef(int index)
        => ref _values[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ref T GetRefOrNullRef(int index)
        => ref index >= _values.Length ? ref Unsafe.NullRef<T>() : ref _values[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Clear() => Array.Clear(_values);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Dispose() {}
}