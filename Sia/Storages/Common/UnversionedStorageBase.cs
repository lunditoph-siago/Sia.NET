namespace Sia;

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

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
    public void Release(scoped in StorageSlot slot)
    {
        int index = slot.Index;

        _allocatedSlots.Remove(index);
        Release(index);

        if (index < _firstFreeSlot) {
            _firstFreeSlot = index;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(scoped in StorageSlot slot)
        => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(scoped in StorageSlot slot)
        => ref GetRef(slot.Index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeGetRef(scoped in StorageSlot slot)
        => ref GetRef(slot.Index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanOwner<T> Fetch(ReadOnlySpan<StorageSlot> slots)
    {
        var spanOwner = SpanOwner<T>.Allocate(slots.Length);
        var span = spanOwner.Span;

        int i = 0;
        foreach (var slot in slots) {
            span[i] = GetRef(slot);
            i++;
        }

        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanOwner<T> UnsafeFetch(ReadOnlySpan<StorageSlot> slots)
    {
        var spanOwner = SpanOwner<T>.Allocate(slots.Length);
        var span = spanOwner.Span;

        int i = 0;
        foreach (var slot in slots) {
            span[i] = GetRef(slot.Index);
            i++;
        }

        return spanOwner;
    }

    public void Write(ReadOnlySpan<StorageSlot> slots, ReadOnlySpan<T> values)
    {
        int i = 0;
        foreach (var slot in slots) {
            GetRef(slot) = values[i];
            i++;
        }
    }

    public void UnsafeWrite(ReadOnlySpan<StorageSlot> slots, ReadOnlySpan<T> values)
    {
        int i = 0;
        foreach (var slot in slots) {
            GetRef(slot.Index) = values[i];
            i++;
        }
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