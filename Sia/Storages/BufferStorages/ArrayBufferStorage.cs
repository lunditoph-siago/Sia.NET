namespace Sia;

public sealed class ArrayBufferStorage<T>(int capacity = 4096)
    : BufferStorage<T, ArrayBuffer<BufferStorageEntry<T>>>(new(capacity))
    where T : struct;