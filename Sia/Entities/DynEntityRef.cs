namespace Sia;

public sealed class DynEntityRef : IDisposable
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

            current.Dispose();
            return (creator.CreateEntity(bundle),
                new EntityMover<Bundle<TEntity, TComponent>>(creator));
        }
    }

    public EntityRef Current => _currRef;
    public object Boxed => _currRef.Boxed;
    public bool Valid => _currRef.Valid;

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

    public bool Contains<TComponent>()
        => _currRef.Contains<TComponent>();

    public bool Contains(Type componentType)
        => _currRef.Contains(componentType);

    public ref TComponent Get<TComponent>()
        => ref _currRef.Get<TComponent>();
    
    public ref TComponent GetOrNullRef<TComponent>()
        => ref _currRef.GetOrNullRef<TComponent>();

    public void Dispose()
        => _currRef.Dispose();
}
