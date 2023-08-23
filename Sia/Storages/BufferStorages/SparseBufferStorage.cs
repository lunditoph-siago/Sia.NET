namespace Sia;

public sealed class SparseBufferStorage<T>
    : BufferStorage<T, SparseBuffer<BufferStorageEntry<T>>>
    where T : struct
{
    public SparseBufferStorage(int capacity = 65535, int pageSize = 256)
        : base(new(capacity, pageSize))
    {
    }
}