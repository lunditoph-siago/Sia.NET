using System.Runtime.CompilerServices;

namespace Sia;

public sealed class ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => Array.MaxLength;

    public int Count {
        get;
        set {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value == field) return;
            if (value > _array.Length) Array.Resize(ref _array, CalculateArraySize(value));
            field = value;
        }
    }

    private T[] _array = new T[initialCapacity];

    public ArrayBuffer() : this(0) {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateArraySize(int requiredCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredCapacity, Array.MaxLength);
        var size = 8;
        while (size < requiredCapacity && size <= Array.MaxLength / 2) { size *= 2; }
        return Math.Max(size, requiredCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
        => ref _array[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRefOrNullRef(int index)
        => ref index < 0 || index >= _array.Length ? ref Unsafe.NullRef<T>() : ref _array[index];

    public Span<T> AsSpan() => _array;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}
