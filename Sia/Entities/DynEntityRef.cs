namespace Sia;

public struct DynEntityRef : IDisposable
{
    private interface IEntityMover
    {
        (EntityRef, IEntityMover) Add<TComponent>(in EntityRef current, in TComponent newComponent);
    }

    private class EntityMover<TEntity>(IEntityCreator creator) : IEntityMover
        where TEntity : struct
    {
        public (EntityRef, IEntityMover) Add<TComponent>(in EntityRef current, in TComponent newComponent)
        {
            var bundle = Bundle.Create(
                current.UnsafeCast<TEntity>().AsRef(), newComponent);

            var id = current.Slot.Id;
            current.Dispose();

            var newEntity = creator.CreateEntity(bundle);
            newEntity.Host.UnsafeSetId(newEntity.Slot, id);

            return (newEntity, new EntityMover<Bundle<TEntity, TComponent>>(creator));
        }
    }

    public readonly EntityRef Current => _currRef;
    public readonly object Boxed => _currRef.Boxed;
    public readonly bool Valid => _currRef.Valid;

    private EntityRef _currRef;
    private IEntityMover _mover;

    public static DynEntityRef Create<TEntity>(EntityRef<TEntity> initial, IEntityCreator creator)
        where TEntity : struct
        => new(initial, new EntityMover<TEntity>(creator));

    private DynEntityRef(EntityRef initial, IEntityMover mover)
    {
        _currRef = initial;
        _mover = mover;
    }

    public unsafe void Add<TComponent>(in TComponent newComponent = default!)
        => (_currRef, _mover) = _mover.Add(_currRef, newComponent);

    public readonly bool Contains<TComponent>()
        => _currRef.Contains<TComponent>();

    public readonly bool Contains(Type componentType)
        => _currRef.Contains(componentType);

    public readonly ref TComponent Get<TComponent>()
        => ref _currRef.Get<TComponent>();
    
    public readonly ref TComponent GetOrNullRef<TComponent>()
        => ref _currRef.GetOrNullRef<TComponent>();

    public readonly void Dispose()
        => _currRef.Dispose();
}
