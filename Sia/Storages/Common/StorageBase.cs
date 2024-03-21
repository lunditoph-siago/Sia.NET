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
    private readonly List<short> _versions = [];

    private int _firstFreeSlot;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StorageSlot AllocateSlot()
    {
        var index = _firstFreeSlot;
        if (index >= Capacity) {
            throw new IndexOutOfRangeException("Storage is full");
        }

        Allocate(index);

        short version;
        int versionCount = _versions.Count;

        if (index == versionCount) {
            version = 1;
            _versions.Add(1);
            _firstFreeSlot++;
        }
        else {
            ref var versionRef = ref _versions.AsSpan()[index];
            version = versionRef = (short)(-versionRef + 1);
            while (++_firstFreeSlot < versionCount && _versions[_firstFreeSlot] > 0) {}
        }

        var slot = new StorageSlot(index, version);
        _allocatedSlots.Add(index, slot);
        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(scoped in StorageSlot slot)
    {
        var index = slot.Index;
        var version = slot.Version;

        ref var versionRef = ref _versions.AsSpan()[index];
        if (versionRef != version) {
            throw new ArgumentException("Bad slot access");
        }

        versionRef = (short)-versionRef;
        _allocatedSlots.Remove(index);
        Release(index);

        if (index < _firstFreeSlot) {
            _firstFreeSlot = index;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(scoped in StorageSlot slot)
        => slot.Index < _versions.Count && _versions[slot.Index] == slot.Version;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(scoped in StorageSlot slot)
    {
        GuardSlotVersion(slot);
        return ref GetRef(slot.Index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(scoped in StorageSlot slot)
        => ref GetRef(slot.Index);

    protected abstract void Allocate(int slot);
    protected abstract void Release(int slot);
    protected abstract ref T GetRef(int slot);

    public abstract void Dispose();

    public IEnumerator<StorageSlot> GetEnumerator()
    {
        foreach (var slot in _allocatedSlots.Values) {
            yield return slot;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GuardSlotVersion(scoped in StorageSlot slot)
    {
        if (_versions[slot.Index] != slot.Version) {
            throw new ArgumentException("Bad slot access");
        }
    }
}