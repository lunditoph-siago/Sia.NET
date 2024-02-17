namespace Sia;

public sealed class UnversionedSparseBufferStorage<T>(int pageSize = 256)
    : UnversionedBufferStorage<T, SparseBuffer<T>>(new(pageSize))
    where T : struct;