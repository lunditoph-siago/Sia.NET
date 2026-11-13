using System.Runtime.CompilerServices;

namespace Sia;

public sealed class ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => Array.MaxLength;

    public int Count {
        get => _count;
        set {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            EnsureCapacity(value);
            _count = value;
        }
    }

    private int _count;
    private T[] _array = new T[initialCapacity];

    public ArrayBuffer() : this(0)
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureCapacity(int count)
    {
        if (count > _array.Length) {
            Grow(count);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int count)
        => Array.Resize(ref _array, GetAllocationSize(count));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendReserved(in T value)
    {
        _array[_count] = value;
        _count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetAllocationSize(int requiredCapacity)
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
    public void Dispose() { }
}
