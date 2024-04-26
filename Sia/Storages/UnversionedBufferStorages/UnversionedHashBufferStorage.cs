namespace Sia;

public sealed class UnversionedHashBufferStorage<T>()
    : UnversionedBufferStorage<T, HashBuffer<T>>(new())
    where T : struct
{
    public override void GetSiblingStorageType<U>(IStorageTypeHandler<U> handler)
        => handler.Handle<UnversionedHashBufferStorage<U>>();
}