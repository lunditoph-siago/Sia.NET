namespace Sia;

public sealed class SparseBufferStorage<T>(int pageSize)
    : BufferStorage<T, SparseBuffer<T>>(new(pageSize))
    where T : struct
{
    public SparseBufferStorage() : this(256) {}

    public override void GetSiblingStorageType<U>(IStorageTypeHandler<U> handler)
        => handler.Handle<SparseBufferStorage<U>>();
}