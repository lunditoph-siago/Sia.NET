namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;

public sealed class BucketBuffer<T>(int bucketCapacity = 256) : IBuffer<T>
{
    private struct Bucket(int capacity)
    {
        public MemoryOwner<T> Memoery = MemoryOwner<T>.Allocate(capacity);
        public int RefCount = 1;
    }
    
    public int Capacity => int.MaxValue;
    public int BucketCapacity { get; } = bucketCapacity;

    private readonly List<Bucket?> _buckets = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T CreateRef(int index)
    {
        int bucketIndex = index / BucketCapacity;
        if (bucketIndex >= _buckets.Count) {
            CollectionsMarshal.SetCount(_buckets, bucketIndex + 1);
        }

        ref var bucket = ref _buckets.AsSpan()[bucketIndex];
        bucket = bucket == null
            ? new(BucketCapacity)
            : bucket.Value with { RefCount = bucket.Value.RefCount + 1 };
        
        return ref bucket.Value.Memoery.Span[index % BucketCapacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
    {
        int bucketIndex = index / BucketCapacity;
        ref var bucket = ref _buckets.AsSpan()[bucketIndex];
        return ref bucket!.Value.Memoery.Span[index % BucketCapacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
    {
        int bucketIndex = index / BucketCapacity;
        return bucketIndex < _buckets.Count && _buckets[bucketIndex] != null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Release(int index)
    {
        int bucketIndex = index / BucketCapacity;
        if (bucketIndex >= _buckets.Count) {
            return false;
        }

        ref var bucket = ref _buckets.AsSpan()[bucketIndex];
        if (bucket is not Bucket bucketValue) {
            return false;
        }

        var refCount = bucketValue.RefCount;
        if (refCount == 1) {
            bucketValue.Memoery.Dispose();
            bucket = null;
        }
        else {
            bucketValue.Memoery.Span[index % BucketCapacity] = default!;
            bucket = bucketValue with { RefCount = refCount - 1 };
        }
        return true;
    }

    public void Dispose()
    {
    }
}