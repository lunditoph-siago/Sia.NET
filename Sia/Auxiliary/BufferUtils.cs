namespace Sia;

using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

public static class BufferUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MemoryOwner<T> Expand<T>(MemoryOwner<T> owner)
    {
        var newOwner = MemoryOwner<T>.Allocate(owner.Length * 2, AllocationMode.Clear);
        owner.Span.CopyTo(newOwner.Span);
        return newOwner;
    }
}