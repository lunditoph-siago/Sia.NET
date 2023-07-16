namespace Sia;

public class UnmanagedEntityFactory<T> : IEntityFactory<T>, IEntityDisposer
    where T : unmanaged
{
    public static UnmanagedEntityFactory<T> Default {
        get {
            s_defaultFactory ??= new(UnmanagedHeapStorage<T>.Instance);
            return s_defaultFactory;
        }
    }

    public IStorage<T> Storage { get; }

    private static UnmanagedEntityFactory<T>? s_defaultFactory;

    public UnmanagedEntityFactory(IStorage<T> storage)
    {
        Storage = storage;
    }

    public EntityRef Create()
    {
        var ptr = Storage.Allocate();
        return new EntityRef(ptr.Raw, UnmanagedEntityAccessor<T>.Instance, this);
    }

    public EntityRef Create(in T initial)
    {
        var ptr = Storage.Allocate(initial);
        return new(ptr.Raw, UnmanagedEntityAccessor<T>.Instance, this);
    }

    public void DisposeEntity(long pointer)
        => Storage.UnsafeRelease(pointer);
}