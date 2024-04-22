namespace Sia;

public sealed class HashBufferStorage<T>()
    : BufferStorage<T, HashBuffer<T>>(new())
    where T : struct
{
    public override void CreateSiblingStorage<U>(IStorageHandler<U> handler)
        => handler.Handle<HashBufferStorage<U>>(new());
}