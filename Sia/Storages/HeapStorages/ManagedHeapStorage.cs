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
    public int Count => _entries.Count;
    public bool IsManaged => true;

    private Dictionary<nint, Box<BufferStorageEntry<T>>> _entries = [];
    private ObjectIDGenerator _idGenerator = new();

    private ManagedHeapStorage() {}

    public nint UnsafeAllocate(out int version)
        => UnsafeAllocate(default, out version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public nint UnsafeAllocate(in T initial, out int version)
    {
        Box<BufferStorageEntry<T>> entry = new BufferStorageEntry<T> {
            Version = 1,
            Value = initial
        };
        nint id = (nint)_idGenerator.GetId(entry, out bool _);
        _entries[id] = entry;
        version = 1;
        return id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(nint rawPointer, int version)
    {
        if (version != 1 || !_entries.Remove(rawPointer)) {
            throw new ArgumentException("Invalid pointer");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(nint rawPointer, int version)
    {
        if (version != 1 || !_entries.TryGetValue(rawPointer, out var entry)) {
            throw new ArgumentException("Invalid pointer");
        }
        return ref entry.GetReference().Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
    {
        foreach (var key in _entries.Keys) {
            handler(key, 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
    {
        foreach (var key in _entries.Keys) {
            handler(data, key, 1);
        }
    }

    public IEnumerator<(nint, int)> GetEnumerator()
    {
        foreach (var key in _entries.Keys) {
            yield return (key, 1);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public void Dispose()
    {
        _entries = null!;
        _idGenerator = null!;
    }
}