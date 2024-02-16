namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct EntityRef(StorageSlot Slot, IEntityHost Host) : IDisposable
{
    public object Boxed => Host.Box(Slot);
    public bool Valid => Host != null && Host.IsValid(Slot);
    public EntityDescriptor Descriptor => Host.GetDescriptor(Slot);

    public EntityRef<TEntity> Cast<TEntity>()
        where TEntity : struct
    {
        var descriptor = Host.GetDescriptor(Slot);
        if (typeof(TEntity) != descriptor.Type) {
            throw new InvalidCastException(
                $"Cannot cast entity type from {descriptor.Type} to {typeof(TEntity)}");
        }
        return new(Slot, Host);
    }

    public EntityRef<TEntity> UnsafeCast<TEntity>()
        where TEntity : struct
        => new(Slot, Host);
    
    public bool Contains<TComponent>()
        => Host.Contains<TComponent>(Slot);

    public bool Contains(Type componentType)
        => Host.Contains(Slot, componentType);

    public ref TComponent Get<TComponent>()
        => ref Host.Get<TComponent>(Slot);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Host.GetOrNullRef<TComponent>(Slot);

    public Span<byte> AsSpan()
        => Host.GetSpan(Slot);

    public readonly void Dispose()
        => Host.Release(Slot);
}

public readonly record struct EntityRef<TEntity>(StorageSlot Slot, IEntityHost Host)
    : IDisposable
    where TEntity : struct
{
    public object Boxed => Host.Box(Slot);
    public bool Valid => Host != null && Host.IsValid(Slot);

    public unsafe ref TEntity AsRef()
        => ref Unsafe.AsRef<TEntity>(Unsafe.AsPointer(ref AsSpan()[0]));

    public unsafe EntityRef<Bundle<TEntity, TComponent>> Add<TComponent>(
        in TComponent newComponent, IEntityCreator creator)
    {
        var bundle = Bundle.Create(AsRef(), newComponent);
        return creator.CreateEntity(bundle);
    }

    public unsafe EntityRef<TEntity> Move(IEntityCreator creator)
    {
        var data = AsRef();
        Dispose();
        return creator.CreateEntity(data);
    }

    public unsafe EntityRef<Bundle<TEntity, TComponent>> Move<TComponent>(
        in TComponent newComponent, IEntityCreator creator)
    {
        var bundle = Bundle.Create(AsRef(), newComponent);
        Dispose();
        return creator.CreateEntity(bundle);
    }
    
    public bool Contains<TComponent>()
        => Host.Contains<TComponent>(Slot);

    public bool Contains(Type componentType)
        => Host.Contains(Slot, componentType);

    public ref TComponent Get<TComponent>()
        => ref Host.Get<TComponent>(Slot);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Host.GetOrNullRef<TComponent>(Slot);

    public Span<byte> AsSpan()
        => Host.GetSpan(Slot);

    public readonly void Dispose()
        => Host.Release(Slot);
    
    public static implicit operator EntityRef(in EntityRef<TEntity> entity)
        => new(entity.Slot, entity.Host);
}