namespace Sia;

public interface IEntityHost : IEnumerable<Entity>, IDisposable
{
    event Action<IEntityHost>? OnDisposed;

    Type InnerEntityType { get; }
    EntityDescriptor Descriptor { get; }

    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get; }

    Entity Create();
    void Release(in StorageSlot slot);
    void MoveOut(in StorageSlot slot);
    bool IsValid(in StorageSlot slot);

    ref byte GetByteRef(in StorageSlot slot);
    ref byte UnsafeGetByteRef(in StorageSlot slot);

    void GetHList<THandler>(in StorageSlot slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>;

    Entity Add<TComponent>(in StorageSlot slot, in TComponent initial);
    Entity AddMany<TList>(in StorageSlot slot, in TList list)
        where TList : IHList;
    Entity Set<TComponent>(in StorageSlot slot, in TComponent value);

    Entity Remove<TComponent>(in StorageSlot slot, out bool success);
    Entity RemoveMany<TList>(in StorageSlot slot)
        where TList : IHList;

    object Box(in StorageSlot slot);
}

public interface IReactiveEntityHost : IEntityHost
{
    event EntityHandler? OnEntityCreated;
    event EntityHandler? OnEntityReleased;
}

public interface IEntityHost<TEntity> : IEntityHost
    where TEntity : IHList
{
    Entity Create(in TEntity initial);
    void MoveIn(in HList<Entity, TEntity> data);

    ref HList<Entity, TEntity> GetRef(in StorageSlot slot);
    ref HList<Entity, TEntity> UnsafeGetRef(in StorageSlot slot);
}