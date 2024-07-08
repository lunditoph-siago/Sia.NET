namespace Sia;

using System.Collections;
using System.Runtime.CompilerServices;

public static class BufferEntityHost<TEntity>
    where TEntity : IHList
{
    public static BufferEntityHost<TEntity, TBuffer> Create<TBuffer>(TBuffer Buffer)
        where TBuffer : IBuffer<TEntity>
        => new(Buffer);
}

public class BufferEntityHost<TEntity, TBuffer>(TBuffer Buffer) : IEntityHost<TEntity>
    where TEntity : IHList
    where TBuffer : IBuffer<TEntity>
{
    public event Action<IEntityHost>? OnDisposed;

    public Type EntityType => typeof(TEntity);
    public EntityDescriptor Descriptor { get; } = EntityDescriptor.Get<TEntity>();

    public int Capacity => Buffer.Capacity;
    public int Count => Buffer.Count;

    public TBuffer Buffer { get; } = Buffer;

    private readonly List<Entity> _entities = [];

    public virtual Entity Create() => Create(default!);
    public virtual Entity Create(in TEntity initial)
    {
        var e = Entity.Pool.Get();
        MoveIn(e, initial);
        return e;
    }

    public virtual void Release(int slot)
    {
        var entity = _entities[slot];
        entity.Host = null!;
        Entity.Pool.Return(entity);
        MoveOut(slot);
    }

    public Entity GetEntity(int slot)
        => _entities[slot];

    public void MoveOut(int slot)
    {
        int lastSlot = Buffer.Count - 1;
        if (slot != lastSlot) {
            Buffer.GetRef(slot) = Buffer.GetRef(lastSlot);
            var lastEntity = _entities[lastSlot];
            lastEntity.Slot = slot;
            _entities[slot] = lastEntity;
        }
        Buffer.GetRef(lastSlot) = default!;
        _entities.RemoveAt(lastSlot);
        Buffer.Count--;
    }

    public void MoveIn(Entity entity, in TEntity data)
    {
        int slot = Buffer.Count++;
        Buffer.GetRef(slot) = data;

        entity.Host = this;
        entity.Slot = slot;
        _entities.Add(entity);
    }

    public unsafe ref byte GetByteRef(int slot, out Entity entity)
        => ref Unsafe.As<TEntity, byte>(ref GetRef(slot, out entity));

    public unsafe ref byte GetByteRef(int slot)
        => ref Unsafe.As<TEntity, byte>(ref GetRef(slot));

    public ref TEntity GetRef(int slot)
        => ref Buffer.GetRef(slot);

    public ref TEntity GetRef(int slot, out Entity entity)
    {
        ref var data = ref Buffer.GetRef(slot);
        entity = _entities[slot];
        return ref data!;
    }

    public void GetHList<THandler>(int slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => handler.Handle(ref Buffer.GetRef(slot));

    public virtual Entity Add<TComponent>(int slot, in TComponent initial)
    {
        if (EntityIndexer<TEntity, TComponent>.Offset != -1) {
            EntityExceptionHelper.ThrowComponentExisted<TComponent>();
        }
        ref var data = ref Buffer.GetRef(slot);
        var entity = _entities[slot];

        var host = entity.Host.GetSiblingHost<HList<TComponent, TEntity>>();
        host.MoveIn(entity, HList.Cons(initial, data));

        MoveOut(slot);
        return entity;
    }

    private struct EntityMover(int slot, Entity e)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T data)
            where T : IHList
        {
            var host = e.Host;
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

    public virtual Entity AddMany<TList>(int slot, in TList list)
        where TList : IHList
    {
        TList.HandleTypes(EntityComponentChecker.Instance);

        ref var data = ref Buffer.GetRef(slot);
        var entity = _entities[slot];

        var mover = new EntityMover(slot, entity);
        data.Concat(list, mover);
        return entity;
    }

    public virtual Entity Set<TComponent>(int slot, in TComponent value)
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

    public virtual Entity Remove<TComponent>(int slot, out bool success)
    {
        ref var data = ref Buffer.GetRef(slot);
        var entity = _entities[slot];

        if (EntityIndexer<TEntity, TComponent>.Offset == -1) {
            success = false;
            return entity;
        }

        var mover = new EntityMover(slot, entity);
        data.Remove(TypeProxy<TComponent>._, mover);
        success = true;
        return entity;
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
        int slot, Entity e)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
        {
            if (typeof(T) == typeof(TEntity)) {
                return;
            }
            var host = e.Host;
            var siblingHost = host.GetSiblingHost<T>();
            host.MoveOut(slot);
            siblingHost.MoveIn(e, value);
        }
    }

    public virtual Entity RemoveMany<TList>(int slot)
        where TList : IHList
    {
        ref var data = ref Buffer.GetRef(slot);
        var entity = _entities[slot];

        data.Filter(
            EntityComponentPredicate<TList>.Instance,
            new FilteredHListMover(slot, entity));
        return entity;
    }

    public virtual IEntityHost<UEntity> GetSiblingHost<UEntity>()
        where UEntity : IHList
        => throw new NotSupportedException("Sibling host not supported");

    public virtual void GetSiblingHostType<UEntity>(IGenericConcreteTypeHandler<IEntityHost<UEntity>> hostTypeHandler)
        where UEntity : IHList
        => throw new NotSupportedException("Sibling host not supported");

    public object Box(int slot)
        => Buffer.GetRef(slot);

    public IEnumerator<Entity> GetEnumerator() => _entities.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        Buffer.Dispose();
        OnDisposed?.Invoke(this);
        GC.SuppressFinalize(this);
    }
}