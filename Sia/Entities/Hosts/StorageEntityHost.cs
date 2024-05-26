namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;

public class StorageEntityHost<TEntity>
    where TEntity : IHList
{
    public StorageEntityHost<TEntity, TStorage> Create<TStorage>(TStorage storage)
        where TStorage : IStorage<HList<Entity, TEntity>>
        => new(storage);
}

public class StorageEntityHost<TEntity, TStorage>(TStorage storage) : IEntityHost<TEntity>
    where TEntity : IHList
    where TStorage : IStorage<HList<Entity, TEntity>>
{
    public event Action<IEntityHost>? OnDisposed;

    public Type InnerEntityType => typeof(TEntity);
    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<HList<Entity, TEntity>>();

    public int Capacity => Storage.Capacity;
    public int Count => Storage.Count;
    public ReadOnlySpan<StorageSlot> AllocatedSlots => Storage.AllocatedSlots;

    public TStorage Storage { get; } = storage;

    public virtual Entity Create()
    {
        var e = Entity.Get();
        var slot = Storage.AllocateSlot(HList.Cons(e, default(TEntity)!));
        e.Slot = slot;
        e.Host = this;
        return e;
    }

    public virtual Entity Create(in TEntity initial)
    {
        var e = Entity.Get();
        var slot = Storage.AllocateSlot(HList.Cons(e, initial));
        e.Slot = slot;
        e.Host = this;
        return e;
    }

    public virtual void Release(in StorageSlot slot)
        => Storage.Release(slot);

    public void MoveOut(in StorageSlot slot)
        => Storage.Release(slot);

    public void MoveIn(in HList<Entity, TEntity> data)
    {
        var slot = Storage.AllocateSlot(data);
        var e = data.Head;
        e.Host = this;
        e.Slot = slot;
    }

    public bool IsValid(in StorageSlot slot)
        => Storage.IsValid(slot);

    public unsafe ref byte GetByteRef(in StorageSlot slot)
        => ref Unsafe.As<HList<Entity, TEntity>, byte>(ref Storage.GetRef(slot));

    public unsafe ref byte UnsafeGetByteRef(in StorageSlot slot)
        => ref Unsafe.As<HList<Entity, TEntity>, byte>(ref Storage.UnsafeGetRef(slot));

    public ref HList<Entity, TEntity> GetRef(in StorageSlot slot)
        => ref Storage.GetRef(slot);

    public ref HList<Entity, TEntity> UnsafeGetRef(in StorageSlot slot)
        => ref Storage.UnsafeGetRef(slot);

    public void GetHList<THandler>(in StorageSlot slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => handler.Handle(ref Storage.GetRef(slot));

    public virtual Entity Add<TComponent>(in StorageSlot slot, in TComponent initial)
    {
        if (EntityIndexer<HList<Entity, TEntity>, TComponent>.Offset != -1) {
            EntityExceptionHelper.ThrowComponentExisted<TComponent>();
        }
        var host = GetSiblingHost<HList<TComponent, TEntity>>();
        ref var entity = ref Storage.GetRef(slot);
        var head = entity.Head;
        var newData = HList.Cons(head, HList.Cons(initial, entity.Tail));
        MoveOut(slot);
        host.MoveIn(newData);
        return head;
    }

    private struct EntityMover(
        StorageEntityHost<TEntity, TStorage> host, StorageSlot slot, Entity e)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<TNewEntity>(in TNewEntity entity)
            where TNewEntity : IHList
        {
            var siblingHost = host.GetSiblingHost<TNewEntity>();
            var newData = HList.Cons(e, entity);
            host.MoveOut(slot);
            siblingHost.MoveIn(newData);
        }
    }

    private struct EntityComponentChecker : IGenericTypeHandler
    {
        public static EntityComponentChecker Instance = new();

        public readonly void Handle<T>()
        {
            if (EntityIndexer<HList<Entity, TEntity>, T>.Offset != -1) {
                EntityExceptionHelper.ThrowComponentExisted<T>();
            }
        }
    }

    public virtual Entity AddMany<TList>(in StorageSlot slot, in TList list)
        where TList : IHList
    {
        TList.HandleTypes(EntityComponentChecker.Instance);
        ref var entity = ref Storage.GetRef(slot);
        var head = entity.Head;
        var mover = new EntityMover(this, slot, head);
        entity.Tail.Concat(list, mover);
        return head;
    }

    public virtual Entity Set<TComponent>(in StorageSlot slot, in TComponent value)
    {
        var offset = EntityIndexer<HList<Entity, TEntity>, TComponent>.Offset;
        if (offset == -1) {
            return Add(slot, value);
        }
        ref var entity = ref GetRef(slot);
        Unsafe.As<HList<Entity, TEntity>, TComponent>(
            ref Unsafe.AddByteOffset(ref entity, offset))
            = value;
        return entity.Head;
    }

    public virtual Entity Remove<TComponent>(in StorageSlot slot, out bool success)
    {
        ref var entity = ref Storage.GetRef(slot);
        var head = entity.Head;
        if (EntityIndexer<HList<Entity, TEntity>, TComponent>.Offset == -1) {
            success = false;
            return head;
        }
        var mover = new EntityMover(this, slot, head);
        entity.Tail.Remove(TypeProxy<TComponent>._, mover);
        success = true;
        return head;
    }

    private readonly struct EntityComponentPredicate<TList> : IGenericPredicate
        where TList : IHList
    {
        public static EntityComponentPredicate<TList> Instance = new();

        private struct TypeCollector(HashSet<Type> set) : IGenericTypeHandler
        {
            public readonly void Handle<T>()
                => set.Add(typeof(T));
        }

        private readonly HashSet<Type> _types = [];

        public EntityComponentPredicate()
            => TList.HandleTypes(new TypeCollector(_types));

        public readonly bool Predicate<T>(in T value)
            => !_types.Contains(typeof(T));
    }

    private unsafe struct FilteredHListMover(
        StorageEntityHost<TEntity, TStorage> host, StorageSlot slot, Entity e)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
        {
            if (typeof(T) == typeof(TEntity)) {
                return;
            }
            var siblingHost = host.GetSiblingHost<T>();
            host.MoveOut(slot);
            siblingHost.MoveIn(HList.Cons(e, value));
        }
    }

    public virtual Entity RemoveMany<TList>(in StorageSlot slot)
        where TList : IHList
    {
        ref var entity = ref Storage.GetRef(slot);
        var head = entity.Head;
        entity.Tail.Filter(
            EntityComponentPredicate<TList>.Instance,
            new FilteredHListMover(this, slot, head));
        return head;
    }

    protected virtual IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : IHList
        => throw new NotSupportedException("Sibling host not supported");

    public object Box(in StorageSlot slot)
        => Storage.GetRef(slot);

    public IEnumerator<Entity> GetEnumerator()
    {
        foreach (var slot in Storage) {
            yield return GetRef(slot).Head;
        }
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        Storage.Dispose();
        OnDisposed?.Invoke(this);
        GC.SuppressFinalize(this);
    }
}