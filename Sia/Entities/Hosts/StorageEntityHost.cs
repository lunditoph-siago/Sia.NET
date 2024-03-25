namespace Sia;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public class StorageEntityHost<TEntity>
    where TEntity : IHList
{
    public StorageEntityHost<TEntity, TStorage> Create<TStorage>(TStorage storage)
        where TStorage : IStorage<HList<Identity, TEntity>>
        => new(storage);
}

public class StorageEntityHost<TEntity, TStorage>(TStorage managedStorage) : IEntityHost<TEntity>
    where TEntity : IHList
    where TStorage : IStorage<HList<Identity, TEntity>>
{
    public event Action? OnDisposed;

    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<HList<Identity, TEntity>>();

    public int Capacity => Storage.Capacity;
    public int Count => Storage.Count;
    public ReadOnlySpan<StorageSlot> AllocatedSlots => Storage.AllocatedSlots;

    public TStorage Storage { get; } = managedStorage;

    public bool ContainsCommon<TComponent>()
        => Descriptor.GetOffset<TComponent>() != -1;

    public bool ContainsCommon(Type componentType)
        => Descriptor.GetOffset(componentType) != -1;

    public virtual EntityRef Create()
        => new(Storage.AllocateSlot(HList.Cons(Identity.Create(), default(TEntity)!)), this);

    public virtual EntityRef Create(in TEntity initial)
        => new(Storage.AllocateSlot(HList.Cons(Identity.Create(), initial)), this);

    public virtual void Release(in StorageSlot slot)
        => Storage.Release(slot);

    public virtual void MoveOut(in StorageSlot slot)
        => Storage.Release(slot);

    public virtual EntityRef MoveIn(in HList<Identity, TEntity> data)
    {
        var slot = Storage.AllocateSlot(data);
        return new(slot, this);
    }

    public bool IsValid(in StorageSlot slot)
        => Storage.IsValid(slot);

    public unsafe ref byte GetByteRef(in StorageSlot slot)
        => ref Unsafe.As<HList<Identity, TEntity>, byte>(ref Storage.GetRef(slot));

    public unsafe ref byte UnsafeGetByteRef(in StorageSlot slot)
        => ref Unsafe.As<HList<Identity, TEntity>, byte>(ref Storage.UnsafeGetRef(slot));

    public ref HList<Identity, TEntity> GetRef(in StorageSlot slot)
        => ref Storage.GetRef(slot);

    public ref HList<Identity, TEntity> UnsafeGetRef(in StorageSlot slot)
        => ref Storage.UnsafeGetRef(slot);

    public virtual EntityRef Add<TComponent>(in StorageSlot slot, in TComponent initial)
    {
        var host = GetSiblingHost<HList<TComponent, TEntity>>();
        ref var entity = ref Storage.GetRef(slot);
        var newEntity = host.MoveIn(HList.Cons(entity.Head, HList.Cons(initial, entity.Tail)));
        MoveOut(slot);
        return newEntity;
    }

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    private unsafe struct EntityMover(
        StorageEntityHost<TEntity, TStorage> host, Identity id, EntityRef* result)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<TNewEntity>(in TNewEntity entity)
            where TNewEntity : IHList
        {
            var siblingHost = host.GetSiblingHost<TNewEntity>();
            *result = siblingHost.MoveIn(HList.Cons(id, entity));
        }
    }

    public virtual unsafe EntityRef AddMany<TList>(in StorageSlot slot, in TList list)
        where TList : IHList
    {
        ref var entity = ref Storage.GetRef(slot);
        EntityRef result;
        var mover = new EntityMover(this, entity.Head, &result);
        list.Concat(entity.Tail, mover);
        MoveOut(slot);
        return result;
    }

    public virtual unsafe EntityRef Remove<TComponent>(in StorageSlot slot)
    {
        ref var entity = ref Storage.GetRef(slot);
        EntityRef result;
        var mover = new EntityMover(this, entity.Head, &result);
        entity.Tail.Remove(TypeProxy<TComponent>.Default, mover);
        MoveOut(slot);
        return result;
    }

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    protected virtual IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : IHList
        => throw new NotSupportedException("Sibling host not supported");

    public object Box(in StorageSlot slot)
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