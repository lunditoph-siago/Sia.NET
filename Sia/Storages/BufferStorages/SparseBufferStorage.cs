namespace Sia;

public sealed class SparseBufferStorage<T>(int pageSize)
    : BufferStorage<T, SparseBuffer<T>>(new(pageSize))
    where T : struct
{
    public SparseBufferStorage() : this(256) {}

    public override void CreateSiblingStorage<U>(IStorageHandler<U> handler)
        => handler.Handle<SparseBufferStorage<U>>(new(pageSize));
}