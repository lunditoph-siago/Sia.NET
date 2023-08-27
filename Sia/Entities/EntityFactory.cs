using System.Runtime.CompilerServices;

namespace Sia
{

public class EntityFactory<T>
    where T : struct
{
    public EntityFactory<T, TStorage> Create<TStorage>(TStorage storage)
        where TStorage : class, IStorage<T>
        => new(storage);
}

public sealed class EntityFactory<T, TStorage>
    : Internal.EntityFactory<T, StorageWrapper<T, TStorage>>
    where T : struct
    where TStorage : class, IStorage<T>
{
    public EntityFactory(TStorage managedStorage)
        : base(new(managedStorage))
    {
    }
}

namespace Internal
{
    public class EntityFactory<T, TStorage> : IEntityFactory<T>, IEntityAccessor, IEntityDisposer
        where T : struct
        where TStorage : IStorage<T>
    {
        public static EntityDescriptor Descriptor { get; }
            = EntityDescriptor.Get<T>();

        public TStorage Storage { get; }

        internal EntityFactory(TStorage managedStorage)
        {
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
                (void*)((IntPtr)Unsafe.AsPointer(ref entity) + offset));
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
}

}