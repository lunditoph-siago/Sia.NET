using System.Diagnostics;

namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;

public readonly struct BucketBuffer<T>(int bucketCapacity) : IBuffer<T>
{
    private struct Bucket(int capacity)
    {
        public T[] Memory = new T[capacity];
        public int RefCount = 1;
    }
    
    public int Capacity => int.MaxValue;
    public int BucketCapacity { get; } = bucketCapacity;

    public ref T this[int index]
    {
        get
        {
            int bucketIndex = index / BucketCapacity;
            CheckElementReadAccess(bucketIndex);
            if (bucketIndex >= _buckets.Count) return ref Unsafe.NullRef<T>();
            ref var bucket = ref _buckets.AsSpan()[bucketIndex];
            IsNotNull(bucket, bucketIndex);
            if (bucket == null) return ref Unsafe.NullRef<T>();
            return ref bucket.Value.Memory[index % BucketCapacity];
        }
    }

    private readonly List<Bucket?> _buckets = [];

    public BucketBuffer() : this(256) {}

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
        
        return ref bucket.Value.Memory[index % BucketCapacity];
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
        
        bucketValue.Memory[index % BucketCapacity] = default!;

        var refCount = bucketValue.RefCount;
        if (refCount == 1) {
            bucket = null;
        }
        else {
            bucket = bucketValue with { RefCount = refCount - 1 };
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
    {
        int bucketIndex = index / BucketCapacity;
        return bucketIndex < _buckets.Count && _buckets.AsSpan()[bucketIndex] != null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _buckets.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}

    [Conditional("ENABLE_COLLECTIONS_CHECKS")]
    private void CheckElementReadAccess(int index)
    {
        if (index < 0 || index >= _buckets.Count)
            throw new IndexOutOfRangeException($"Index {(object)index} is out of range of '{(object)_buckets.Count}' Length.");
    }

    [Conditional("ENABLE_COLLECTIONS_CHECKS")]
    private void IsNotNull(Bucket? item, int index)
    {
        if (item is null)
            throw new IndexOutOfRangeException($"Element at index {(object)index} is null");
    }
}