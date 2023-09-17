namespace Sia;

using System.Runtime.CompilerServices;

public interface IStorage : IDisposable
{
    int Capacity { get; }
    int Count { get; }
    int PointerValidBits { get; }
    bool IsManaged { get; }

    long UnsafeAllocate();
    void UnsafeRelease(long rawPointer);

    void IterateAllocated(StoragePointerHandler handler);
    void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler);
}

public interface IStorage<T> : IStorage
    where T : struct
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    long UnsafeAllocate(in T initial)
    {
        long pointer = UnsafeAllocate();
        UnsafeGetRef(pointer) = initial;
        return pointer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate() => new(UnsafeAllocate(), this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Pointer<T> Allocate(in T initial) => new(UnsafeAllocate(initial), this);

    ref T UnsafeGetRef(long rawPointer);
}