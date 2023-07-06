namespace Sia;

public class EntityFactory<T>
{
    public static EntityFactory<T> Native {
        get {
            s_nativeFactory ??= new(NativeStorage<T>.Instance);
            return s_nativeFactory;
        }
    }
    private static EntityFactory<T>? s_nativeFactory;

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