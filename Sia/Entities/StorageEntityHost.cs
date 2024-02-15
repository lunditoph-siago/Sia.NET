using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sia
{

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
    {
        var ptr = Storage.Allocate();
        return new(ptr.Slot, ptr.Version, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual EntityRef<T> Create(in T initial)
    {
        var ptr = Storage.UnsafeAllocate(initial, out int version);
        return new(ptr, version, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Release(int slot, int version)
        => Storage.UnsafeRelease(slot, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(int slot, int version)
        => Storage.IsValid(slot, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>(int slot, int version) => ContainsCommon<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int slot, int version, Type componentType) => ContainsCommon(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityDescriptor GetDescriptor(int slot, int version)
        => Descriptor;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent Get<TComponent>(int slot, int version)
    {
        ref var entity = ref Storage.UnsafeGetRef(slot, version);
        var offset = EntityIndexer<T, TComponent>.Offset
            ?? throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent GetOrNullRef<TComponent>(int slot, int version)
    {
        ref var entity = ref Storage.UnsafeGetRef(slot, version);
        var offset = EntityIndexer<T, TComponent>.Offset;
        if (!offset.HasValue) {
            return ref Unsafe.NullRef<TComponent>();
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset.Value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object Box(int slot, int version)
        => Storage.UnsafeGetRef(slot, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe Span<byte> GetSpan(int slot, int version)
        => new(Unsafe.AsPointer(ref Storage.UnsafeGetRef(slot, version)), Descriptor.MemorySize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<EntityRef> GetEnumerator()
    {
        foreach (var (slot, version) in Storage) {
            yield return new(slot, version, this);
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

}