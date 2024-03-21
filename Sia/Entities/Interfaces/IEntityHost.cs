namespace Sia;

public interface IEntityHost : IEnumerable<EntityRef>, IDisposable
{
    event Action? OnDisposed;

    EntityDescriptor Descriptor { get; }

    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get; }

    EntityRef Create();
    void Release(scoped in StorageSlot slot);
    void MoveOut(scoped in StorageSlot slot);
    bool IsValid(scoped in StorageSlot slot);

    ref byte GetByteRef(scoped in StorageSlot slot);
    ref byte UnsafeGetByteRef(scoped in StorageSlot slot);

    EntityRef Add<TComponent>(scoped in StorageSlot slot, in TComponent initial);
    EntityRef AddBundle<TBundle>(scoped in StorageSlot slot, in TBundle bundle)
        where TBundle : IHList;
    EntityRef Remove<TComponent>(scoped in StorageSlot slot);

    object Box(scoped in StorageSlot slot);
}

public interface IReactiveEntityHost : IEntityHost
{
    event EntityHandler? OnEntityCreated;
    event EntityHandler? OnEntityReleased;
}

public interface IEntityHost<TEntity> : IEntityHost
    where TEntity : IHList
{
    EntityRef Create(in TEntity initial);
    EntityRef MoveIn(in HList<Identity, TEntity> data);

    ref HList<Identity, TEntity> GetRef(scoped in StorageSlot slot);
    ref HList<Identity, TEntity> UnsafeGetRef(scoped in StorageSlot slot);
}