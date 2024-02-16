namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;

public sealed class ArrayBuffer<T>(int initialCapacity = 0) : IBuffer<T>
{
    public int Capacity => int.MaxValue;

    private readonly List<T> _values = new(initialCapacity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T CreateRef(int index)
    {
        if (index >= _values.Count) {
            CollectionsMarshal.SetCount(_values, index + 1);
        }
        return ref _values.AsSpan()[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Release(int index)
    {
        if (index >= _values.Count) {
            return false;
        }
        _values[index] = default!;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
        => index < _values.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
        => ref _values.AsSpan()[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRefOrNullRef(int index)
        => ref index >= _values.Count ? ref Unsafe.NullRef<T>() : ref _values.AsSpan()[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear() => _values.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}