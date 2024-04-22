namespace Sia;

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public abstract class UnversionedStorageBase<T> : IStorage<T>
    where T : struct
{
    public abstract int Capacity { get; }

    public int Count => _allocatedSlots.Count;
    public ReadOnlySpan<StorageSlot> AllocatedSlots => _allocatedSlots.ValueSpan;

    private readonly SparseSet<StorageSlot> _allocatedSlots = [];

    private int _firstFreeSlot;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StorageSlot AllocateSlot()
    {
        var index = _firstFreeSlot;
        if (index >= Capacity) {
            throw new IndexOutOfRangeException("Storage is full");
        }

        Allocate(index);
        while (_allocatedSlots.ContainsKey(++_firstFreeSlot)) {}

        var slot = new StorageSlot(index, 1);
        _allocatedSlots.Add(index, slot);
        return slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(in StorageSlot slot)
    {
        int index = slot.Index;

        _allocatedSlots.Remove(index);
        Release(index);

        if (index < _firstFreeSlot) {
            _firstFreeSlot = index;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(in StorageSlot slot)
        => true;

    public ref T GetRef(scoped in StorageSlot slot)
        => ref GetRef(slot.Index);

    public ref T UnsafeGetRef(scoped in StorageSlot slot)
        => ref GetRef(slot.Index);

    protected abstract void Allocate(int slot);
    protected abstract void Release(int slot);
    protected abstract ref T GetRef(int slot);

    public virtual void CreateSiblingStorage<U>(IStorageHandler<U> handler)
        where U : struct
        => throw new NotImplementedException("CreateSiblingStorage not implemented for this storage");

    public abstract void Dispose();

    public IEnumerator<StorageSlot> GetEnumerator()
    {
        foreach (var slot in _allocatedSlots.Values) {
            yield return slot;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}