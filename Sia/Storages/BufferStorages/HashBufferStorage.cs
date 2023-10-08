namespace Sia;

public sealed class HashBufferStorage<T>
    : BufferStorage<T, HashBuffer<T>>
    where T : struct
{
    public HashBufferStorage() : base(new())
    {
    }
}