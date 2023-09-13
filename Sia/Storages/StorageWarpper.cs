namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// see https://github.com/dotnet/runtime/issues/32815
public readonly struct StorageWrapper<T, TStorage> : IStorage<T>, IEquatable<StorageWrapper<T, TStorage>>
    where T : struct
    where TStorage : class, IStorage<T>
{
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

    public StorageWrapper(TStorage storage)
    {
        _storage = storage;
    }

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
    public bool Equals(StorageWrapper<T, TStorage> other)
        => _storage == other._storage;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(Action<long> func)
        => _storage.IterateAllocated(func);

    public override bool Equals([AllowNull] object obj)
        => obj is StorageWrapper<T, TStorage> wrapper
            && Equals(wrapper);

    public override int GetHashCode()
        => _storage.GetHashCode();

    public static bool operator ==(StorageWrapper<T, TStorage> left, StorageWrapper<T, TStorage> right)
        => left.Equals(right);

    public static bool operator !=(StorageWrapper<T, TStorage> left, StorageWrapper<T, TStorage> right)
        => !(left == right);
}