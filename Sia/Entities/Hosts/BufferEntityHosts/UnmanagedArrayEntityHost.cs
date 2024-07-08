namespace Sia;

public class UnmanagedArrayEntityHost<TEntity>(int initialCapacity)
    : BufferEntityHost<TEntity, UnmanagedArrayBuffer<TEntity>>(new UnmanagedArrayBuffer<TEntity>(initialCapacity))
    where TEntity : IHList
{
    public UnmanagedArrayEntityHost() : this(0) {}

    public override void GetSiblingHostType<UEntity>(
        IGenericConcreteTypeHandler<IEntityHost<UEntity>> hostTypeHandler)
        => hostTypeHandler.Handle<UnmanagedArrayEntityHost<UEntity>>();
}