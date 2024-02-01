namespace Sia;

using System.Runtime.CompilerServices;

public sealed class ArrayBuffer<T>(int capacity) : IBuffer<T>
{
    public int Capacity => _values.Length;

    private readonly T[] _values = new T[capacity];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T CreateRef(int index)
        => ref _values[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
        => ref _values[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
        => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Release(int index)
    {
        _values[index] = default!;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
    }
}