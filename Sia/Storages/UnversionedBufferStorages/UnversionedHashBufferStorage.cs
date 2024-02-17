namespace Sia;

public sealed class UnversionedHashBufferStorage<T>()
    : UnversionedBufferStorage<T, HashBuffer<T>>(new())
    where T : struct;