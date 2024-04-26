namespace Sia;

public sealed class UnversionedSparseBufferStorage<T>(int pageSize)
    : UnversionedBufferStorage<T, SparseBuffer<T>>(new(pageSize))
    where T : struct
{
    public UnversionedSparseBufferStorage() : this(256) {}

    public override void GetSiblingStorageType<U>(IStorageTypeHandler<U> handler)
        => handler.Handle<UnversionedSparseBufferStorage<U>>();
}