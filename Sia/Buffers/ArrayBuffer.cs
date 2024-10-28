namespace Sia;

using System.Runtime.CompilerServices;

public sealed class ArrayBuffer<T>(int initialCapacity) : IBuffer<T>
{
    public bool IsManaged => true;
    public int Capacity => int.MaxValue;

    public int Count {
        get => _count;
        set {
            if (value == _count) return;
            if (value > _array.Length) Array.Resize(ref _array, CalculateArraySize(value));
            _count = value;
        }
    }

    private int _count;
    private T[] _array = new T[initialCapacity];

    public ArrayBuffer() : this(0) {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateArraySize(int requiredCapacity)
    {
        int size = 8;
        while (size < requiredCapacity) { size *= 2; }
        return size;
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