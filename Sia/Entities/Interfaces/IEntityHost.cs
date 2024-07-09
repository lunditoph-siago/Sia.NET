namespace Sia;

public interface IEntityHost : IEnumerable<Entity>, IDisposable
{
    event Action<IEntityHost>? OnDisposed;

    Type EntityType { get; }
    EntityDescriptor Descriptor { get; }

    int Capacity { get; }
    int Count { get; }

    Entity Create();
    void Release(int slot);
    void MoveOut(int slot);

    Entity GetEntity(int slot);

    ref byte GetByteRef(int slot);
    ref byte GetByteRef(int slot, out Entity entity);

    void GetHList<THandler>(int slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>;

    Entity Add<TComponent>(int slot, in TComponent initial);
    Entity AddMany<TList>(int slot, in TList list)
        where TList : struct, IHList;
    Entity Set<TComponent>(int slot, in TComponent value);

    Entity Remove<TComponent>(int slot, out bool success);
    Entity RemoveMany<TList>(int slot)
        where TList : struct, IHList;

    object Box(int slot);

    IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : struct, IHList;
    void GetSiblingHostType<UEntity>(
        IGenericConcreteTypeHandler<IEntityHost<UEntity>> hostTypeHandler)
        where UEntity : struct, IHList;
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
    ref TEntity GetRef(int slot, out Entity entity);
}