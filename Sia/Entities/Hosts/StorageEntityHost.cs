namespace Sia;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public class StorageEntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>
    where TEntity : struct
{
    public StorageEntityHost<TEntity, TStorage> Create<TStorage>(TStorage storage)
        where TStorage : IStorage<WithId<TEntity>>
        => new(storage);
}

public class StorageEntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, TStorage>(TStorage managedStorage) : IEntityHost<TEntity>
    where TEntity : struct
    where TStorage : IStorage<WithId<TEntity>>
{
    public event Action? OnDisposed;

    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<WithId<TEntity>>();

    public int Capacity => Storage.Capacity;
    public int Count => Storage.Count;
    public ReadOnlySpan<StorageSlot> AllocatedSlots => Storage.AllocatedSlots;

    public TStorage Storage { get; } = managedStorage;

    public bool ContainsCommon<TComponent>()
        => Descriptor.GetOffset<TComponent>() != -1;

    public bool ContainsCommon(Type componentType)
        => Descriptor.GetOffset(componentType) != -1;

    EntityRef IEntityHost.Create() => Create();

    public virtual EntityRef<WithId<TEntity>> Create()
        => new(Storage.AllocateSlot(new WithId<TEntity> {
            Identity = Identity.Create()
        }), this);

    public virtual EntityRef<WithId<TEntity>> Create(in TEntity initial)
        => new(Storage.AllocateSlot(new WithId<TEntity> {
            Identity = Identity.Create(),
            Entity = initial
        }), this);

    public virtual void Release(scoped in StorageSlot slot)
        => Storage.Release(slot);

    public bool IsValid(scoped in StorageSlot slot)
        => Storage.IsValid(slot);

    public ref byte GetByteRef(scoped in StorageSlot slot)
        => ref Unsafe.As<WithId<TEntity>, byte>(ref Storage.GetRef(slot));

    public ref byte UnsafeGetByteRef(scoped in StorageSlot slot)
        => ref Unsafe.As<WithId<TEntity>, byte>(ref Storage.UnsafeGetRef(slot));

    public ref WithId<TEntity> GetRef(scoped in StorageSlot slot)
        => ref Storage.GetRef(slot);

    public ref WithId<TEntity> UnsafeGetRef(scoped in StorageSlot slot)
        => ref Storage.UnsafeGetRef(slot);

    public object Box(scoped in StorageSlot slot)
        => Storage.GetRef(slot);

    public IEnumerator<EntityRef> GetEnumerator()
    {
        foreach (var slot in Storage) {
            yield return new(slot, this);
        }
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        Storage.Dispose();
        OnDisposed?.Invoke();
        GC.SuppressFinalize(this);
    }
}