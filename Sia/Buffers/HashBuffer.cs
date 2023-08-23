namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class HashBuffer<T> : IBuffer<T>
{
    public int Capacity => int.MaxValue;
    public int Count => _dict.Count;

    private Dictionary<int, T> _dict = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetOrAddValueRef(int index, out bool exists)
        => ref CollectionsMarshal.GetValueRefOrAddDefault(_dict, index, out exists)!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetValueRefOrNullRef(int index)
        => ref CollectionsMarshal.GetValueRefOrNullRef(_dict, index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(int index)
        => _dict.Remove(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        _dict = null!;
    }
}