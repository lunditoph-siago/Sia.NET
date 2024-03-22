namespace Sia;

public interface IEntityHost : IEnumerable<EntityRef>, IDisposable
{
    event Action? OnDisposed;

    EntityDescriptor Descriptor { get; }

    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get; }

    EntityRef Create();
    void Release(in StorageSlot slot);
    void MoveOut(in StorageSlot slot);
    bool IsValid(in StorageSlot slot);

    ref byte GetByteRef(in StorageSlot slot);
    ref byte UnsafeGetByteRef(in StorageSlot slot);

    EntityRef Add<TComponent>(in StorageSlot slot, in TComponent initial);
    EntityRef AddMany<TBundle>(in StorageSlot slot, in TBundle bundle)
        where TBundle : IHList;
    EntityRef AddBundle<TBundle>(in StorageSlot slot, in TBundle bundle)
        where TBundle : IBundle;
    EntityRef Remove<TComponent>(in StorageSlot slot);

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
    EntityRef Create(in TEntity initial);
    EntityRef MoveIn(in HList<Identity, TEntity> data);

    ref HList<Identity, TEntity> GetRef(in StorageSlot slot);
    ref HList<Identity, TEntity> UnsafeGetRef(in StorageSlot slot);
}