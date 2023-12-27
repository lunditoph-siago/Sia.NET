namespace Sia;

using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public sealed class BucketBuffer<T>(int bucketCapacity = 256) : IBuffer<T>
{
    public int Capacity => int.MaxValue;
    public int BucketCapacity { get; } = bucketCapacity;

    private readonly List<T[]?> _buckets = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(int index)
    {
        int bucketIndex = index / BucketCapacity;

        if (bucketIndex >= _buckets.Count) {
            _buckets.EnsureCapacity(bucketIndex + 1);
            while (bucketIndex >= _buckets.Count) {
                _buckets.Add(null);
            }
        }

        ref var bucket = ref _buckets.AsSpan()[bucketIndex];
        bucket ??= new T[BucketCapacity];
        return ref bucket[index % BucketCapacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAllocated(int index)
    {
        int bucketIndex = index / BucketCapacity;
        return bucketIndex < _buckets.Count && _buckets[bucketIndex] != null;
    }

    public void Dispose()
    {
    }
}