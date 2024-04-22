namespace Sia;

public sealed class UnversionedHashBufferStorage<T>()
    : UnversionedBufferStorage<T, HashBuffer<T>>(new())
    where T : struct
{
    public override void CreateSiblingStorage<U>(IStorageHandler<U> handler)
        => handler.Handle<UnversionedHashBufferStorage<U>>(new());
}