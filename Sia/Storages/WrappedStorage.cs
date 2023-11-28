namespace Sia;

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// see https://github.com/dotnet/runtime/issues/32815
public readonly struct WrappedStorage<T, TStorage> : IStorage<T>, IEquatable<WrappedStorage<T, TStorage>>
    where T : struct
    where TStorage : class, IStorage<T>
{
    public TStorage InnerStorage => _storage;

    public int Capacity {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storage.Capacity;
    }
    public int Count {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storage.Count;
    }
    public int PointerValidBits {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storage.PointerValidBits;
    }
    public bool IsManaged {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storage.IsManaged;
    }

    private readonly TStorage _storage;

    public WrappedStorage(TStorage storage)
    {
        _storage = storage;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long UnsafeAllocate()
        => _storage.UnsafeAllocate();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long UnsafeAllocate(in T initial)
        => _storage.UnsafeAllocate(initial);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pointer<T> Allocate()
        => _storage.Allocate();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pointer<T> Allocate(in T initial)
        => _storage.Allocate(initial);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(long rawPointer)
        => _storage.UnsafeRelease(rawPointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(long rawPointer)
        => ref _storage.UnsafeGetRef(rawPointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
        => _storage.Dispose();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(WrappedStorage<T, TStorage> other)
        => _storage == other._storage;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
        => _storage.IterateAllocated(handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
        => _storage.IterateAllocated(data, handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<long> GetEnumerator()
        => _storage.GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerator IEnumerable.GetEnumerator()
        => _storage.GetEnumerator();

    public override bool Equals([AllowNull] object obj)
        => obj is WrappedStorage<T, TStorage> wrapper
            && Equals(wrapper);

    public override int GetHashCode()
        => _storage.GetHashCode();

    public static bool operator ==(WrappedStorage<T, TStorage> left, WrappedStorage<T, TStorage> right)
        => left.Equals(right);

    public static bool operator !=(WrappedStorage<T, TStorage> left, WrappedStorage<T, TStorage> right)
        => !(left == right);
}