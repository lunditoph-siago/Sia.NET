namespace Sia;

public sealed class ArrayBufferStorage<T>(int initialCapacity = 0)
    : BufferStorage<T, ArrayBuffer<T>>(new(initialCapacity))
    where T : struct;