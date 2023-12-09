namespace Sia;

using System.Runtime.Serialization;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;
using System.Collections.Generic;
using System.Collections;

public sealed class ManagedHeapStorage<T> : IStorage<T>
    where T : struct
{
    public static ManagedHeapStorage<T> Instance { get; } = new();

    public int Capacity { get; } = int.MaxValue;
    public int Count => _objects.Count;
    public int PointerValidBits => 32;
    public bool IsManaged => true;

    private Dictionary<long, Box<T>> _objects = [];
    private ObjectIDGenerator _idGenerator = new();

    private ManagedHeapStorage() {}

    public long UnsafeAllocate()
        => UnsafeAllocate(default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long UnsafeAllocate(in T initial)
    {
        Box<T> obj = initial;
        long id = _idGenerator.GetId(obj, out bool _);
        _objects[id] = obj;
        return id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(long rawPointer)
    {
        if (!_objects.Remove(rawPointer)) {
            throw new ArgumentException("Invalid pointer");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(long rawPointer)
        => ref _objects[rawPointer].GetReference();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
    {
        foreach (var key in _objects.Keys) {
            handler(key);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
    {
        foreach (var key in _objects.Keys) {
            handler(data, key);
        }
    }

    public IEnumerator<long> GetEnumerator()
        => _objects.Keys.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public void Dispose()
    {
        _objects = null!;
        _idGenerator = null!;
    }
}