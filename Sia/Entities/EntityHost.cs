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
        return new(ptr.Raw, ptr.Version, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual EntityRef Create(in T initial)
    {
        var ptr = Storage.UnsafeAllocate(initial, out int version);
        return new(ptr, version, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Release(nint pointer, int version)
        => Storage.UnsafeRelease(pointer, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>(nint pointer, int version)
        => Descriptor.Contains<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(nint pointer, int version, Type type)
        => Descriptor.Contains(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent Get<TComponent>(nint pointer, int version)
    {
        ref var entity = ref Storage.UnsafeGetRef(pointer, version);
        if (!Descriptor.TryGetOffset<TComponent>(out var offset)) {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent GetOrNullRef<TComponent>(nint pointer, int version)
    {
        ref var entity = ref Storage.UnsafeGetRef(pointer, version);
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
    public object Box(nint pointer, int version)
        => Storage.UnsafeGetRef(pointer, version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<EntityRef> GetEnumerator()
    {
        foreach (var (pointer, version) in Storage) {
            yield return new EntityRef(pointer, version, this);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}

}