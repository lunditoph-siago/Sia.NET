namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;

public sealed class BucketBuffer<T>(int bucketCapacity = 256) : IBuffer<T>
{
    private struct Bucket(int capacity)
    {
        public T[] Memory = new T[capacity];
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
        
        return ref bucket.Value.Memory[index % BucketCapacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
    {
        int bucketIndex = index / BucketCapacity;
        ref var bucket = ref _buckets.AsSpan()[bucketIndex];
        return ref bucket!.Value.Memory[index % BucketCapacity];
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRefOrNullRef(int index)
    {
        int bucketIndex = index / BucketCapacity;
        if (bucketIndex >= _buckets.Count) {
            return ref Unsafe.NullRef<T>();
        }
        ref var bucket = ref _buckets.AsSpan()[bucketIndex];
        if (bucket == null) {
            return ref Unsafe.NullRef<T>();
        }
        return ref bucket.Value.Memory[index % BucketCapacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
    {
        int bucketIndex = index / BucketCapacity;
        return bucketIndex < _buckets.Count && _buckets.AsSpan()[bucketIndex] != null;
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
    public void Clear()
    {
        _buckets.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {}
}