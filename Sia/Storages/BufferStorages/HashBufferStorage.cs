namespace Sia;

public sealed class HashBufferStorage<T>()
    : BufferStorage<T, HashBuffer<T>>(new())
    where T : struct
{
    public override void GetSiblingStorageType<U>(IStorageTypeHandler<U> handler)
        => handler.Handle<HashBufferStorage<U>>();
}