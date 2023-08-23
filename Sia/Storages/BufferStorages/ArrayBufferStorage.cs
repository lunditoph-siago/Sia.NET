namespace Sia;

public sealed class ArrayBufferStorage<T>
    : BufferStorage<T, ArrayBuffer<BufferStorageEntry<T>>>
    where T : struct
{
    public ArrayBufferStorage(int capacity = 4096)
        : base(new(capacity))
    {
    }
}