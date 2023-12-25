namespace Sia;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class HashBuffer<T> : IBuffer<T>
{
    public int Capacity => int.MaxValue;

    private readonly Dictionary<int, T> _dict = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
        => ref CollectionsMarshal.GetValueRefOrAddDefault(_dict, index, out bool _)!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
        => _dict.ContainsKey(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
    }
}