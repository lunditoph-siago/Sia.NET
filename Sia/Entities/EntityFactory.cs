namespace Sia;

using System.Runtime.CompilerServices;

public class EntityFactory<T> : IEntityFactory<T>, IEntityAccessor, IEntityDisposer
    where T : struct
{
    public static EntityFactory<T> Default {
        get {
            s_defaultFactory ??= new(ManagedHeapStorage<T>.Instance);
            return s_defaultFactory;
        }
    }

    public static EntityDescriptor Descriptor { get; }
        = EntityDescriptor.Get<T>();

    public IStorage<T> Storage { get; }

    private static EntityFactory<T>? s_defaultFactory;

    public EntityFactory(IStorage<T> managedStorage)
    {
        if (!managedStorage.IsManaged) {
            throw new ArgumentException("Managed storage required");
        }
        Storage = managedStorage;
    }

    public EntityRef Create()
    {
        var ptr = Storage.Allocate();
        return new(ptr.Raw, this, this);
    }

    public EntityRef Create(in T initial)
    {
        var ptr = Storage.Allocate(initial);
        return new(ptr.Raw, this, this);
    }

    public void DisposeEntity(long pointer)
        => Storage.UnsafeRelease(pointer);

    public bool Contains<TComponent>(long pointer)
        => Descriptor.Contains<TComponent>();

    public bool Contains(long pointer, Type type)
        => Descriptor.Contains(type);

    public unsafe ref TComponent Get<TComponent>(long pointer)
    {
        ref var entity = ref Storage.UnsafeGetRef(pointer);
        if (!Descriptor.TryGetOffset<TComponent>(out var offset)) {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer<T>(ref entity) + offset));
    }

    public unsafe ref TComponent GetOrNullRef<TComponent>(long pointer)
    {
        ref var entity = ref Storage.UnsafeGetRef(pointer);
        if (!Descriptor.TryGetOffset<TComponent>(out var offset)) {
            return ref Unsafe.NullRef<TComponent>();
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset));
    }
}