namespace Sia;

public sealed class SparseBufferStorage<T>(int capacity = 65535, int pageSize = 256)
    : BufferStorage<T, SparseBuffer<BufferStorageEntry<T>>>(new(capacity, pageSize))
    where T : struct;