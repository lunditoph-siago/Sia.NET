namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;

public interface IStorage : IEnumerable, IDisposable
{
    int Capacity { get; }
    int Count { get; }
    bool IsManaged { get; }

    nint UnsafeAllocate(out int version);
    void UnsafeRelease(nint rawPointer, int version);

    void IterateAllocated(StoragePointerHandler handler);
    void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler);
}

public interface IStorage<T> : IStorage, IEnumerable<(nint Pointer, int Version)>
    where T : struct
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    nint UnsafeAllocate(in T initial, out int version)
    {
        nint pointer = UnsafeAllocate(out version);
        UnsafeGetRef(pointer, version) = initial;
        return pointer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate() => new(UnsafeAllocate(out int version), version, this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate(in T initial) => new(UnsafeAllocate(initial, out int version), version, this);

    ref T UnsafeGetRef(nint rawPointer, int version);
}