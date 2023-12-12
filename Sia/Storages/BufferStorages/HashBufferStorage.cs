namespace Sia;

public sealed class HashBufferStorage<T>()
    : BufferStorage<T, HashBuffer<BufferStorageEntry<T>>>(new())
    where T : struct;