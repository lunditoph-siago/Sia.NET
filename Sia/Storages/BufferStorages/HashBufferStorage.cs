namespace Sia;

public sealed class HashBufferStorage<T>()
    : BufferStorage<T, HashBuffer<T>>(new())
    where T : struct;