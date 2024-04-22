namespace Sia;

public sealed class BucketBufferStorage<T>(int bucketCapacity)
    : BufferStorage<T, BucketBuffer<T>>(new(bucketCapacity))
    where T : struct
{
    public BucketBufferStorage() : this(256) {}

    public override void CreateSiblingStorage<U>(IStorageHandler<U> handler)
        => handler.Handle<BucketBufferStorage<U>>(new(bucketCapacity));
}