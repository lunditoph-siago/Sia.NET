namespace Sia;

using System.Runtime.CompilerServices;

public sealed class SparseBuffer<T>(int pageSize = 256) : IBuffer<T>
{
    public int Capacity => int.MaxValue;
    private readonly SparseSet<T> _sparseSet = new(pageSize);

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