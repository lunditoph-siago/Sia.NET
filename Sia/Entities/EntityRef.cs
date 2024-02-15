namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct EntityRef(int Slot, int Version, IEntityHost Host) : IDisposable
{
    public object Boxed => Host.Box(Slot, Version);
    public bool Valid => Host != null && Host.IsValid(Slot, Version);
    public EntityDescriptor Descriptor => Host.GetDescriptor(Slot, Version);

    public EntityRef<TEntity> Cast<TEntity>()
        where TEntity : struct
    {
        var descriptor = Host.GetDescriptor(Slot, Version);
        if (typeof(TEntity) != descriptor.Type) {
            throw new InvalidCastException(
                $"Cannot cast entity type from {descriptor.Type} to {typeof(TEntity)}");
        }
        return new(Slot, Version, Host);
    }

    public EntityRef<TEntity> UnsafeCast<TEntity>()
        where TEntity : struct
        => new(Slot, Version, Host);
    
    public bool Contains<TComponent>()
        => Host.Contains<TComponent>(Slot, Version);

    public bool Contains(Type componentType)
        => Host.Contains(Slot, Version, componentType);

    public ref TComponent Get<TComponent>()
        => ref Host.Get<TComponent>(Slot, Version);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Host.GetOrNullRef<TComponent>(Slot, Version);

    public Span<byte> AsSpan()
        => Host.GetSpan(Slot, Version);

    public readonly void Dispose()
        => Host.Release(Slot, Version);
}

public readonly record struct EntityRef<TEntity>(int Slot, int Version, IEntityHost Host)
    : IDisposable
    where TEntity : struct
{
    public object Boxed => Host.Box(Slot, Version);
    public bool Valid => Host != null && Host.IsValid(Slot, Version);

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
        => Host.Contains<TComponent>(Slot, Version);

    public bool Contains(Type componentType)
        => Host.Contains(Slot, Version, componentType);

    public ref TComponent Get<TComponent>()
        => ref Host.Get<TComponent>(Slot, Version);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Host.GetOrNullRef<TComponent>(Slot, Version);

    public Span<byte> AsSpan()
        => Host.GetSpan(Slot, Version);

    public readonly void Dispose()
        => Host.Release(Slot, Version);
    
    public static implicit operator EntityRef(in EntityRef<TEntity> entity)
        => new(entity.Slot, entity.Version, entity.Host);
}