using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sia
{

public class EntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    where T : struct
{
    public EntityHost<T, TStorage> Create<TStorage>(TStorage storage)
        where TStorage : class, IStorage<T>
        => new(storage);
}

public class EntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T, TStorage> : IEntityHost<T>
    where T : struct
    where TStorage : IStorage<T>
{
    EntityDescriptor IEntityHost.Descriptor => Descriptor;

    public int Capacity => Storage.Capacity;
    public int Count => Storage.Count;

    public static EntityDescriptor Descriptor { get; }
        = EntityDescriptor.Get<T>();

    public TStorage Storage { get; }

    public EntityHost(TStorage managedStorage)
    {
        Storage = managedStorage;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual EntityRef Create()
    {
        var ptr = Storage.Allocate();
        return new(ptr.Raw, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual EntityRef Create(in T initial)
    {
        var ptr = Storage.UnsafeAllocate(initial);
        return new(ptr, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Release(long pointer)
        => Storage.UnsafeRelease(pointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>(long pointer)
        => Descriptor.Contains<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(long pointer, Type type)
        => Descriptor.Contains(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent Get<TComponent>(long pointer)
    {
        ref var entity = ref Storage.UnsafeGetRef(pointer);
        if (!Descriptor.TryGetOffset<TComponent>(out var offset)) {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent GetOrNullRef<TComponent>(long pointer)
    {
        ref var entity = ref Storage.UnsafeGetRef(pointer);
        if (!Descriptor.TryGetOffset<TComponent>(out var offset)) {
            return ref Unsafe.NullRef<TComponent>();
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
        => Storage.IterateAllocated(handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
        => Storage.IterateAllocated(data, handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object Box(long pointer)
        => Storage.UnsafeGetRef(pointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<EntityRef> GetEnumerator()
    {
        foreach (var pointer in Storage) {
            yield return new EntityRef(pointer, this);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}

}