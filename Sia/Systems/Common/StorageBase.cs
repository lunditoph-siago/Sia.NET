namespace Sia;

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public abstract class StorageBase<T> : IStorage<T>
    where T : struct
{
    public abstract int Capacity { get; }

    public int Count => _allocatedSlots.Count;
    public ReadOnlySpan<StorageSlot> AllocatedSlots => _allocatedSlots.ValueSpan;

    private readonly SparseSet<StorageSlot> _allocatedSlots = [];
    private readonly List<int> _versions = [];

    private int _firstFreeSlot;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int UnsafeAllocate(out int version)
    {
        var slot = _firstFreeSlot;
        if (slot >= Capacity) {
            throw new IndexOutOfRangeException("Storage is full");
        }

        Allocate(slot);

        int versionCount = _versions.Count;
        if (slot == versionCount) {
            version = 1;
            _versions.Add(1);
            _firstFreeSlot++;
        }
        else {
            ref var versionRef = ref _versions.AsSpan()[slot];
            version = versionRef = -versionRef + 1;
            while (++_firstFreeSlot < versionCount && _versions[_firstFreeSlot] > 0) {}
        }

        _allocatedSlots.Add(slot, new(slot, version));
        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(int slot, int version)
    {
        ref var versionRef = ref _versions.AsSpan()[slot];
        if (versionRef != version) {
            throw new ArgumentException("Bad slot access");
        }

        versionRef = -versionRef;
        _allocatedSlots.Remove(slot);
        Release(slot);

        if (slot < _firstFreeSlot) {
            _firstFreeSlot = slot;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(int slot, int version)
        => slot < _versions.Count && _versions[slot] == version;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(int slot, int version)
    {
        if (slot >= _versions.Count || _versions[slot] != version) {
            throw new ArgumentException("Bad slot access");
        }
        return ref GetRef(slot);
    }

    protected abstract void Allocate(int slot);
    protected abstract void Release(int slot);
    protected abstract ref T GetRef(int slot);

    public abstract void Dispose();

    public IEnumerator<StorageSlot> GetEnumerator()
    {
        foreach (var (slot, version) in _allocatedSlots.Values) {
            yield return new(slot, version);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}