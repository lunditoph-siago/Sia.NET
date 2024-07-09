namespace Sia;

public interface IEntityHost : IEnumerable<Entity>, IDisposable
{
    event Action<IEntityHost>? OnDisposed;

    Type EntityType { get; }
    EntityDescriptor Descriptor { get; }

    int Capacity { get; }
    int Count { get; }
    int Version { get; }

    Entity Create();
    void Release(Entity entity);
    void MoveOut(Entity entity);

    Entity GetEntity(int slot);
    ref byte GetByteRef(int slot);
    void GetHList<THandler>(int slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>;

    void Add<TComponent>(Entity entity, in TComponent initial);
    void AddMany<TList>(Entity entity, in TList list)
        where TList : struct, IHList;
    void Set<TComponent>(Entity entity, in TComponent value);

    void Remove<TComponent>(Entity entity, out bool success);
    void RemoveMany<TList>(Entity entity)
        where TList : struct, IHList;

    object Box(int slot);

    IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : struct, IHList;
    void GetSiblingHostType<UEntity>(
        IGenericConcreteTypeHandler<IEntityHost<UEntity>> hostTypeHandler)
        where UEntity : struct, IHList;
    
    Span<Entity> UnsafeGetEntitySpan();
}

public interface IReactiveEntityHost : IEntityHost
{
    event EntityHandler? OnEntityCreated;
    event EntityHandler? OnEntityReleased;
}

public interface IEntityHost<TEntity> : IEntityHost
    where TEntity : struct, IHList
{
    Entity Create(in TEntity initial);
    void MoveIn(Entity entity, in TEntity data);
    ref TEntity GetRef(int slot);
}