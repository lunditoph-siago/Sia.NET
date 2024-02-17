namespace Sia;

public sealed class UnversionedBucketBufferStorage<T>(int bucketCapacity = 256)
    : UnversionedBufferStorage<T, BucketBuffer<T>>(new(bucketCapacity))
    where T : struct;