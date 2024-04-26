namespace Sia;

public sealed class UnversionedArrayBufferStorage<T>(int initialCapacity)
    : UnversionedBufferStorage<T, ArrayBuffer<T>>(new(initialCapacity))
    where T : struct
{
    public UnversionedArrayBufferStorage() : this(0) {}

    public override void GetSiblingStorageType<U>(IStorageTypeHandler<U> handler)
        => handler.Handle<UnversionedArrayBufferStorage<U>>();
}