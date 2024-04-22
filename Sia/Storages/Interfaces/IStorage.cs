namespace Sia;

using System.Runtime.CompilerServices;

public interface IStorage : IEnumerable<StorageSlot>, IDisposable
{
    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get; }

    StorageSlot AllocateSlot();
    void Release(in StorageSlot slot);
    bool IsValid(in StorageSlot slot);
}

public interface IStorage<T> : IStorage
    where T : struct
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    StorageSlot AllocateSlot(in T initial)
    {
        var slot = AllocateSlot();
        UnsafeGetRef(slot) = initial;
        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate() => new(AllocateSlot(), this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate(in T initial) => new(AllocateSlot(initial), this);

    ref T GetRef(scoped in StorageSlot slot);
    ref T UnsafeGetRef(scoped in StorageSlot slot);

    void CreateSiblingStorage<U>(IStorageHandler<U> handler)
        where U : struct;
}