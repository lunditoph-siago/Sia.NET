using System.Diagnostics.CodeAnalysis;

namespace Sia;

public static class WorldCommonHostExtensions
{
    public static WorldEntityHost<TEntity, BucketBufferStorage<TEntity>> GetBucketHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetHost<TEntity, BucketBufferStorage<TEntity>>(() => new(bucketCapacity));

    public static EntityRef CreateInBucketHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world, in TEntity initial, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetBucketHost<TEntity>(bucketCapacity).Create(initial);

    public static WorldEntityHost<TEntity, HashBufferStorage<TEntity>> GetHashHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world)
        where TEntity : struct
        => world.GetHost<TEntity, HashBufferStorage<TEntity>>();

    public static EntityRef CreateInHashHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world, in TEntity initial)
        where TEntity : struct
        => world.GetHashHost<TEntity>().Create(initial);

    public static WorldEntityHost<TEntity, ArrayBufferStorage<TEntity>> GetArrayHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world, int capacity)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, ArrayBufferStorage<TEntity>>>(out var host)
            ? host : world.GetHost<TEntity, ArrayBufferStorage<TEntity>>(() => new(capacity));

    public static EntityRef CreateInArrayHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world, in TEntity initial, int capacity)
        where TEntity : struct
        => world.GetArrayHost<TEntity>(capacity).Create(initial);

    public static WorldEntityHost<TEntity, SparseBufferStorage<TEntity>> GetSparseHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world, int capacity = 65535, int pageSize = 256)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, SparseBufferStorage<TEntity>>>(out var host)
            ? host : world.GetHost<TEntity, SparseBufferStorage<TEntity>>(() => new(capacity, pageSize));

    public static EntityRef CreateInSparseHost<TEntity>(this World world, in TEntity initial, int capacity = 65535, int pageSize = 256)
        where TEntity : struct
        => world.GetSparseHost<TEntity>(capacity, pageSize).Create(initial);

    public static WorldEntityHost<TEntity, ManagedHeapStorage<TEntity>> GetManagedHeapHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world)
        where TEntity : struct
        => world.GetHost<TEntity, ManagedHeapStorage<TEntity>>(() => ManagedHeapStorage<TEntity>.Instance);

    public static EntityRef CreateInManagedHeapHost<TEntity>(this World world, in TEntity initial)
        where TEntity : struct
        => world.GetManagedHeapHost<TEntity>().Create(initial);

    public static WorldEntityHost<TEntity, UnmanagedHeapStorage<TEntity>> GetUnmanagedHeapHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world)
        where TEntity : struct
        => world.GetHost<TEntity, UnmanagedHeapStorage<TEntity>>(() => UnmanagedHeapStorage<TEntity>.Instance);
        
    public static EntityRef CreateInUnmanagedHeapHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(this World world, in TEntity initial)
        where TEntity : struct
        => world.GetUnmanagedHeapHost<TEntity>().Create(initial);
}