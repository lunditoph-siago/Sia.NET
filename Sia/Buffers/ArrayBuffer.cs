namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public sealed class ArrayBuffer<T> : IBuffer<T>
{
    public int Capacity => _values.Length;
    public int Count { get; set; }

    private readonly T[] _values;
    private readonly bool[] _allocatedTags;

    public ArrayBuffer(int capacity)
    {
        _values = new T[capacity];
        _allocatedTags = new bool[capacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetOrAddValueRef(int index, out bool exists)
    {
        exists = _allocatedTags[index];
        if (!exists) {
            _allocatedTags[index] = true;
        }
        return ref _values[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetValueRefOrNullRef(int index)
    {
        if (!_allocatedTags[index]) {
            return ref Unsafe.NullRef<T>();
        }
        return ref _values[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int index)
    {
        ref var allocatedTag = ref _allocatedTags[index];
        if (!allocatedTag) {
            return false;
        }
        allocatedTag = false;
        _values[index] = default!;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int index, [MaybeNullWhen(false)] out T value)
    {
        ref var allocatedTag = ref _allocatedTags[index];
        if (!allocatedTag) {
            value = default;
            return false;
        }
        allocatedTag = false;
        value = _values[index];
        _values[index] = default!;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
    }
}