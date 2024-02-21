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

    void UnsafeSetId(scoped in StorageSlot slot, int id);
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
    new EntityRef<T> Create();
    EntityRef<T> Create(in T initial);

    ref T GetRef(scoped in StorageSlot slot);
    ref T UnsafeGetRef(scoped in StorageSlot slot);
}