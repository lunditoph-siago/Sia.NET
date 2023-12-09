namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public abstract class VariableStorage<T, TStorage> : IStorage<T>
    where T : struct
    where TStorage : IStorage<T>, IEquatable<TStorage>
{
    public int Capacity => int.MaxValue;
    public int Count { get; private set; }
    public int PointerValidBits => 64;

    public bool IsManaged {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _storages[0]!.IsManaged;
    }

    private List<TStorage?> _storages = [];
    private Stack<int> _availableStorageIndices = new();

    public VariableStorage()
    {
    }

    protected void CreateFirstStorage()
    {
        _storages.Add(SafeCreateStorage());
        _availableStorageIndices.Push(0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long UnsafeAllocate()
    {
        var (storage, index) = AcquireStorage();
        var pointer = storage.Allocate().Raw;
        if (storage.Count == storage.Capacity) {
            _availableStorageIndices.Pop();
        }
        Count++;
        return pointer << 32 | (uint)index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long UnsafeAllocate(in T initial)
    {
        var (storage, index) = AcquireStorage();
        var pointer = storage.Allocate(initial).Raw;
        if (storage.Count == storage.Capacity) {
            _availableStorageIndices.Pop();
        }
        Count++;
        return pointer << 32 | (uint)index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(long rawPointer)
    {
        var (index, pointer) = DeconstructRawPointer(rawPointer);

        ref var storage = ref CollectionsMarshal.AsSpan(_storages)[index];
        storage!.UnsafeRelease(pointer);

        var count = storage.Count;
        if (count == 0 && index != 0) {
            storage.Dispose();
            storage = default;
            _availableStorageIndices.Push(index);
        }
        else if (count == storage.Capacity - 1) {
            _availableStorageIndices.Push(index);
        }
        Count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
    {
        var count = _storages.Count;
        for (int i = 0; i != count; ++i) {
            _storages[i]?.IterateAllocated(pointer => handler(pointer << 32 | (uint)i));
        }
    }

    public readonly record struct IterationData<TData>(
        TData Data, StoragePointerHandler<TData> Handler, int Index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
    {
        var count = _storages.Count;
        for (int i = 0; i != count; ++i) {
            _storages[i]?.IterateAllocated(
                new IterationData<TData>(data, handler, i),
                static (in IterationData<TData> data, long pointer) =>
                    data.Handler(data.Data, pointer << 32 | (uint)data.Index));
        }
    }

    public IEnumerator<long> GetEnumerator()
    {
        var count = _storages.Count;
        for (int i = 0; i != count; ++i) {
            var storage = _storages[count];
            if (storage == null) {
                continue;
            }
            foreach (var pointer in storage) {
                yield return pointer;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(long rawPointer)
    {
        var (index, pointer) = DeconstructRawPointer(rawPointer);
        return ref _storages[index]!.UnsafeGetRef(pointer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (TStorage, int) AcquireStorage()
    {
        if (_availableStorageIndices.TryPeek(out int storageIndex)) {
            ref var storage = ref CollectionsMarshal.AsSpan(_storages)[storageIndex];
            if (EqualityComparer<TStorage>.Default.Equals(storage, default)) {
                storage = SafeCreateStorage();
            }
            return (storage!, storageIndex);
        }

        storageIndex = _storages.Count;
        var newStorage = SafeCreateStorage();

        _storages.Add(newStorage);
        _availableStorageIndices.Push(storageIndex);
        return (newStorage, storageIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int, int) DeconstructRawPointer(long pointer)
    {
        int storageIndex = (int)(pointer & uint.MaxValue);
        int storagePointer = (int)(pointer >> 32);
        return (storageIndex, storagePointer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TStorage SafeCreateStorage()
    {
        var storage = CreateStorage();
        if (storage.PointerValidBits > 32) {
            throw new InvalidOperationException(
                "Variable storage only supports 32-bit pointer for inner storage");
        }
        return storage;
    }

    protected abstract TStorage CreateStorage();

    public void Dispose()
    {
        foreach (var storage in _storages) {
            if (EqualityComparer<TStorage>.Default.Equals(storage, default)) {
                storage!.Dispose();
            }
        }

        _storages = null!;
        _availableStorageIndices = null!;

        GC.SuppressFinalize(this);
    }
}