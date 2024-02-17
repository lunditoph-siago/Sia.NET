namespace Sia;

public sealed class UnversionedArrayBufferStorage<T>(int initialCapacity = 0)
    : UnversionedBufferStorage<T, ArrayBuffer<T>>(new(initialCapacity))
    where T : struct;