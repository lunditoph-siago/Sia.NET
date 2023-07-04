namespace Sia;

public class EntityFactory<T>
{
    public IStorage<T> Storage { get; }
    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<T>();

    public EntityFactory()
    {
        Storage = new NativeStorage<T>();
    }

    public EntityFactory(IStorage<T> storage)
    {
        Storage = storage;
    }

    public EntityRef Create()
    {
        var ptr = Storage.Allocate();
        return new EntityRef(ptr, Descriptor, Storage);
    }
}