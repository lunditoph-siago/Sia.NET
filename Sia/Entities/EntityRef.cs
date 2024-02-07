namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct EntityRef(nint Pointer, int Version, IEntityHost Host) : IDisposable
{
    public object Boxed => Host.Box(Pointer, Version);
    public bool Valid => Host != null && Host.IsValid(Pointer, Version);

    public EntityRef<TEntity> Cast<TEntity>()
        where TEntity : struct
    {
        if (typeof(TEntity) != Host.Descriptor.Type) {
            throw new InvalidCastException(
                $"Cannot cast entity type from {Host.Descriptor.Type} to {typeof(TEntity)}");
        }
        return new(Pointer, Version, Host);
    }

    public EntityRef<TEntity> UnsafeCast<TEntity>()
        where TEntity : struct
        => new(Pointer, Version, Host);
    
    public bool Contains<TComponent>()
        => Host.Contains<TComponent>(Pointer, Version);

    public bool Contains(Type componentType)
        => Host.Contains(Pointer, Version, componentType);

    public ref TComponent Get<TComponent>()
        => ref Host.Get<TComponent>(Pointer, Version);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Host.GetOrNullRef<TComponent>(Pointer, Version);

    public Span<byte> AsSpan()
        => Host.GetSpan(Pointer, Version);

    public readonly void Dispose()
        => Host.Release(Pointer, Version);
}

public readonly record struct EntityRef<TEntity>(nint Pointer, int Version, IEntityHost Host)
    : IDisposable
    where TEntity : struct
{
    public object Boxed => Host.Box(Pointer, Version);
    public bool Valid => Host != null && Host.IsValid(Pointer, Version);

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
        => Host.Contains<TComponent>(Pointer, Version);

    public bool Contains(Type componentType)
        => Host.Contains(Pointer, Version, componentType);

    public ref TComponent Get<TComponent>()
        => ref Host.Get<TComponent>(Pointer, Version);
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref Host.GetOrNullRef<TComponent>(Pointer, Version);

    public Span<byte> AsSpan()
        => Host.GetSpan(Pointer, Version);

    public readonly void Dispose()
        => Host.Release(Pointer, Version);
    
    public static implicit operator EntityRef(in EntityRef<TEntity> entity)
        => new(entity.Pointer, entity.Version, entity.Host);
}