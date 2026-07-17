namespace Sia;

public class UnmanagedArrayEntityHost<TEntity>(int initialCapacity)
    : BufferEntityHost<TEntity, UnmanagedArrayBuffer<TEntity>>(new UnmanagedArrayBuffer<TEntity>(initialCapacity))
    where TEntity : struct, IHList
{
    public UnmanagedArrayEntityHost() : this(0) {}

    public override void GetSiblingHostType<UEntity, THandler>(in THandler hostTypeHandler)
        => hostTypeHandler.Handle<UnmanagedArrayEntityHost<UEntity>>();
}
