namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public sealed class SparseBuffer<T> : IBuffer<T>
{
    public int Capacity => _sparseSet.Capacity;
    public int Count  => _sparseSet.Count;

    private readonly SparseSet<T> _sparseSet;

    public SparseBuffer(int capacity, int pageSize = 256)
    {
        if (capacity <= pageSize) {
            _sparseSet = new(1, capacity);
        }
        else {
            int pageCount = capacity / pageSize + (capacity % pageSize != 0 ? 1 : 0);
            _sparseSet = new(pageCount, pageSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetOrAddValueRef(int index, out bool exists)
        => ref _sparseSet.GetOrAddValueRef(index, out exists);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetValueRefOrNullRef(int index)
        => ref _sparseSet.GetValueRefOrNullRef(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int index)
        => _sparseSet.Remove(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int index, [MaybeNullWhen(false)] out T value)
        => _sparseSet.Remove(index, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
    }
}