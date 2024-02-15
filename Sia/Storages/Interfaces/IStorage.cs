namespace Sia;

using System.Runtime.CompilerServices;

public interface IStorage : IEnumerable<StorageSlot>, IDisposable
{
    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get; }

    int UnsafeAllocate(out int version);
    void UnsafeRelease(int slot, int version);
    bool IsValid(int slot, int version);
}

public interface IStorage<T> : IStorage
    where T : struct
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int UnsafeAllocate(in T initial, out int version)
    {
        int pointer = UnsafeAllocate(out version);
        UnsafeGetRef(pointer, version) = initial;
        return pointer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate() => new(UnsafeAllocate(out int version), version, this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate(in T initial) => new(UnsafeAllocate(initial, out int version), version, this);

    ref T UnsafeGetRef(int slot, int version);
}