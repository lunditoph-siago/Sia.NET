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

    public Type EntityType => typeof(TEntity);
    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<TEntity>();

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
    {
        ref var data = ref Storage.GetRef(slot);
        data.Head.Release();
        Storage.Release(slot);
    }

    public Entity GetEntity(in StorageSlot slot)
        => Storage.GetRef(slot).Head;

    public void MoveOut(in StorageSlot slot)
        => Storage.Release(slot);

    public void MoveIn(Entity entity, in TEntity data)
    {
        var slot = Storage.AllocateSlot(HList.Cons(entity, data));
        entity.Host = this;
        entity.Slot = slot;
    }

    public bool IsValid(in StorageSlot slot)
        => Storage.IsValid(slot);

    public unsafe ref byte GetByteRef(in StorageSlot slot, out Entity entity)
        => ref Unsafe.As<TEntity, byte>(ref GetRef(slot, out entity));

    public unsafe ref byte GetByteRef(in StorageSlot slot)
        => ref Unsafe.As<TEntity, byte>(ref GetRef(slot));

    public unsafe ref byte UnsafeGetByteRef(in StorageSlot slot)
        => ref Unsafe.As<TEntity, byte>(ref UnsafeGetRef(slot));

    public unsafe ref byte UnsafeGetByteRef(in StorageSlot slot, out Entity entity)
        => ref Unsafe.As<TEntity, byte>(ref UnsafeGetRef(slot, out entity));

    public ref TEntity GetRef(in StorageSlot slot)
        => ref Storage.GetRef(slot).Tail;

    public ref TEntity GetRef(in StorageSlot slot, out Entity entity)
    {
        ref var data = ref Storage.GetRef(slot);
        entity = data.Head;
        return ref data.Tail;
    }

    public ref TEntity UnsafeGetRef(in StorageSlot slot)
        => ref Storage.UnsafeGetRef(slot).Tail;

    public ref TEntity UnsafeGetRef(in StorageSlot slot, out Entity entity)
    {
        ref var data = ref Storage.UnsafeGetRef(slot);
        entity = data.Head;
        return ref data.Tail;
    }

    public void GetHList<THandler>(in StorageSlot slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => handler.Handle(ref Storage.GetRef(slot).Tail);

    public virtual Entity Add<TComponent>(in StorageSlot slot, in TComponent initial)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset != -1) {
            EntityExceptionHelper.ThrowComponentExisted<TComponent>();
        }
        var host = GetSiblingHost<HList<TComponent, TEntity>>();
        ref var entity = ref Storage.GetRef(slot);
        var head = entity.Head;
        host.MoveIn(head, HList.Cons(initial, entity.Tail));
        MoveOut(slot);
        return head;
    }

    private struct EntityMover(
        StorageEntityHost<TEntity, TStorage> host, StorageSlot slot, Entity e)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T data)
            where T : IHList
        {
            var siblingHost = host.GetSiblingHost<T>();
            host.MoveOut(slot);
            siblingHost.MoveIn(e, data);
        }
    }

    private struct EntityComponentChecker : IGenericTypeHandler
    {
        public static EntityComponentChecker Instance = new();

        public readonly void Handle<T>()
        {
            if (EntityIndexer<TEntity, T>.Offset != -1) {
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
        var offset = EntityIndexer<TEntity, TComponent>.Offset;
        if (offset == -1) {
            return Add(slot, value);
        }
        ref var data = ref GetRef(slot, out var e);
        Unsafe.As<TEntity, TComponent>(
            ref Unsafe.AddByteOffset(ref data, offset))
            = value;
        return e;
    }

    public virtual Entity Remove<TComponent>(in StorageSlot slot, out bool success)
    {
        ref var entity = ref Storage.GetRef(slot);
        var head = entity.Head;
        if (EntityIndexer<TEntity, TComponent>.Offset == -1) {
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
            siblingHost.MoveIn(e, value);
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
            yield return Storage.UnsafeGetRef(slot).Head;
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