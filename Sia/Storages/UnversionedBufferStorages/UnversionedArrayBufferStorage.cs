namespace Sia;

public sealed class UnversionedArrayBufferStorage<T>(int initialCapacity)
    : UnversionedBufferStorage<T, ArrayBuffer<T>>(new(initialCapacity))
    where T : struct
{
    public UnversionedArrayBufferStorage() : this(0) {}

    public override void CreateSiblingStorage<U>(IStorageHandler<U> handler)
        => handler.Handle<UnversionedArrayBufferStorage<U>>(new(initialCapacity));
}