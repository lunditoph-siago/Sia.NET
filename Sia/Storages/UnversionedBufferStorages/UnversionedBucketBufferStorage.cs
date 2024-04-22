namespace Sia;

public sealed class UnversionedBucketBufferStorage<T>(int bucketCapacity)
    : UnversionedBufferStorage<T, BucketBuffer<T>>(new(bucketCapacity))
    where T : struct
{
    public UnversionedBucketBufferStorage() : this(256) {}

    public override void CreateSiblingStorage<U>(IStorageHandler<U> handler)
        => handler.Handle<UnversionedBucketBufferStorage<U>>(new(bucketCapacity));
}