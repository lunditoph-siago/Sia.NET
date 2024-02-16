namespace Sia;

using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

public class EntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    where T : struct
{
    public StorageEntityHost<T, TStorage> Create<TStorage>(TStorage storage)
        where TStorage : class, IStorage<T>
        => new(storage);
}

public class StorageEntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T, TStorage>(TStorage managedStorage) : IEntityHost<T>
    where T : struct
    where TStorage : IStorage<T>
{
    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<T>();

    public int Capacity => Storage.Capacity;
    public int Count => Storage.Count;
    public ReadOnlySpan<StorageSlot> AllocatedSlots => Storage.AllocatedSlots;

    public TStorage Storage { get; } = managedStorage;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsCommon<TComponent>()
        => Descriptor.Contains<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsCommon(Type componentType)
        => Descriptor.Contains(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    EntityRef IEntityHost.Create() => Create();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual EntityRef<T> Create()
        => new(Storage.AllocateSlot(), this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual EntityRef<T> Create(in T initial)
        => new(Storage.AllocateSlot(initial), this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Release(StorageSlot slot)
        => Storage.Release(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(StorageSlot slot)
        => Storage.IsValid(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>(StorageSlot slot) => ContainsCommon<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(StorageSlot slot, Type componentType) => ContainsCommon(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityDescriptor GetDescriptor(StorageSlot slot)
        => Descriptor;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent Get<TComponent>(StorageSlot slot)
    {
        ref var entity = ref Storage.GetRef(slot);
        var offset = EntityIndexer<T, TComponent>.Offset
            ?? throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent GetOrNullRef<TComponent>(StorageSlot slot)
    {
        ref var entity = ref Storage.GetRef(slot);
        var offset = EntityIndexer<T, TComponent>.Offset;
        if (!offset.HasValue) {
            return ref Unsafe.NullRef<TComponent>();
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset.Value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanOwner<T> Fetch(ReadOnlySpan<StorageSlot> slots)
        => Storage.Fetch(slots);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanOwner<T> UnsafeFetch(ReadOnlySpan<StorageSlot> slots)
        => Storage.UnsafeFetch(slots);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<StorageSlot> slots, ReadOnlySpan<T> values)
        => Storage.Write(slots, values);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeWrite(ReadOnlySpan<StorageSlot> slots, ReadOnlySpan<T> values)
        => Storage.UnsafeWrite(slots, values);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object Box(StorageSlot slot)
        => Storage.GetRef(slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe Span<byte> GetSpan(StorageSlot slot)
        => new(Unsafe.AsPointer(ref Storage.GetRef(slot)), Descriptor.MemorySize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<EntityRef> GetEnumerator()
    {
        foreach (var slot in Storage) {
            yield return new(slot, this);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        Storage.Dispose();
        GC.SuppressFinalize(this);
    }
}