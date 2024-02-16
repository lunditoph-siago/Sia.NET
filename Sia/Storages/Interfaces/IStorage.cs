namespace Sia;

using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

public interface IStorage : IEnumerable<StorageSlot>, IDisposable
{
    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get; }

    StorageSlot AllocateSlot();
    void Release(StorageSlot slot);
    bool IsValid(StorageSlot slot);
}

public interface IStorage<T> : IStorage
    where T : struct
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    StorageSlot AllocateSlot(in T initial)
    {
        var slot = AllocateSlot();
        GetRef(slot) = initial;
        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate() => new(AllocateSlot(), this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate(in T initial) => new(AllocateSlot(initial), this);

    ref T GetRef(StorageSlot slot);

    SpanOwner<T> Fetch(ReadOnlySpan<StorageSlot> slots);
    SpanOwner<T> UnsafeFetch(ReadOnlySpan<StorageSlot> slots);
}