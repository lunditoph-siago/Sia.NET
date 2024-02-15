namespace Sia;

public sealed class SparseBufferStorage<T>(int pageSize = 256)
    : BufferStorage<T, SparseBuffer<T>>(new(pageSize))
    where T : struct;