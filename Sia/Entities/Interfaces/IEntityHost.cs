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
    bool IsValid(scoped in StorageSlot slot);

    ref byte GetByteRef(scoped in StorageSlot slot);
    ref byte UnsafeGetByteRef(scoped in StorageSlot slot);

    object Box(scoped in StorageSlot slot);
}

public interface IReactiveEntityHost : IEntityHost
{
    event EntityHandler? OnEntityCreated;
    event EntityHandler? OnEntityReleased;
}

public interface IEntityHost<T> : IEntityHost
    where T : struct
{
    new EntityRef<WithId<T>> Create();
    EntityRef<WithId<T>> Create(in T initial);

    ref WithId<T> GetRef(scoped in StorageSlot slot);
    ref WithId<T> UnsafeGetRef(scoped in StorageSlot slot);
}