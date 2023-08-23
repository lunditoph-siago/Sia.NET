namespace Sia;

public sealed class HashBufferStorage<T>
    : BufferStorage<T, HashBuffer<BufferStorageEntry<T>>>
    where T : struct
{
    public HashBufferStorage() : base(new())
    {
    }
}