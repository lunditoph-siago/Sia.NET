namespace Sia;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public class StorageEntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    where T : struct
{
    public StorageEntityHost<T, TStorage> Create<TStorage>(TStorage storage)
        where TStorage : class, IStorage<T>
        => new(storage);
}

public class StorageEntityHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, TStorage>(TStorage managedStorage) : IEntityHost<TEntity>
    where TEntity : struct
    where TStorage : IStorage<TEntity>
{
    public event Action? OnDisposed;

    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<TEntity>();

    public int Capacity => Storage.Capacity;
    public int Count => Storage.Count;
    public ReadOnlySpan<StorageSlot> AllocatedSlots => Storage.AllocatedSlots;

    public TStorage Storage { get; } = managedStorage;

    public bool ContainsCommon<TComponent>()
        => Descriptor.GetOffset<TComponent>() != -1;

    public bool ContainsCommon(Type componentType)
        => Descriptor.GetOffset(componentType) != -1;

    EntityRef IEntityHost.Create() => Create();

    public virtual EntityRef<TEntity> Create()
        => new(Storage.AllocateSlot(), this);

    public virtual EntityRef<TEntity> Create(in TEntity initial)
        => new(Storage.AllocateSlot(initial), this);

    public virtual void Release(scoped in StorageSlot slot)
        => Storage.Release(slot);

    public bool IsValid(scoped in StorageSlot slot)
        => Storage.IsValid(slot);

    public void UnsafeSetId(scoped in StorageSlot slot, int id)
        => Storage.UnsafeSetId(slot, id);

    public unsafe ref byte GetByteRef(scoped in StorageSlot slot)
        => ref Unsafe.As<TEntity, byte>(ref Storage.GetRef(slot));

    public unsafe ref byte UnsafeGetByteRef(scoped in StorageSlot slot)
        => ref Unsafe.As<TEntity, byte>(ref Storage.UnsafeGetRef(slot));

    public ref TEntity GetRef(scoped in StorageSlot slot)
        => ref Storage.GetRef(slot);

    public ref TEntity UnsafeGetRef(scoped in StorageSlot slot)
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