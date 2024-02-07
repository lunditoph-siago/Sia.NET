namespace Sia;

using System.Runtime.CompilerServices;

public sealed class SparseBuffer<T> : IBuffer<T>
{
    public int Capacity => _sparseSet.Capacity;

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
    public ref T CreateRef(int index)
        => ref _sparseSet.GetOrAddValueRef(index, out bool _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
        => ref _sparseSet.GetValueRefOrNullRef(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRefOrNullRef(int index)
        => ref _sparseSet.GetValueRefOrNullRef(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
        => _sparseSet.ContainsKey(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Release(int index)
        => _sparseSet.Remove(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
        => _sparseSet.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
    }
}