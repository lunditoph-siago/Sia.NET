namespace Sia;

public class ArrayEntityHost<TEntity>(int initialCapacity)
    : BufferEntityHost<TEntity, ArrayBuffer<TEntity>>(new ArrayBuffer<TEntity>(initialCapacity))
    where TEntity : struct, IHList
{
    public ArrayEntityHost() : this(0) {}

    public override void GetSiblingHostType<UEntity, THandler>(in THandler hostTypeHandler)
        => hostTypeHandler.Handle<ArrayEntityHost<UEntity>>();
}
