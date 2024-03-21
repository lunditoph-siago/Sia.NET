namespace Sia;

using System.Runtime.CompilerServices;

public readonly struct SparseBuffer<T>(int pageSize) : IBuffer<T>
{
    public int Capacity => int.MaxValue;
    private readonly SparseSet<T> _sparseSet = new(pageSize);

    public ref T this[int index] => ref _sparseSet.GetValueRefOrNullRef(index);

    public SparseBuffer() : this(256) {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T CreateRef(int index)
        => ref _sparseSet.GetOrAddValueRef(index, out bool _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Release(int index)
        => _sparseSet.Remove(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
        => _sparseSet.ContainsKey(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
        => _sparseSet.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}