using System.Diagnostics.CodeAnalysis;

namespace Sia;

public static class WorldCommonHostExtensions
{
    // buffer storages

    public static WorldEntityHost<TEntity, BucketBufferStorage<TEntity>> GetBucketHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetHost<TEntity, BucketBufferStorage<TEntity>>(() => new(bucketCapacity));

    public static EntityRef<TEntity> CreateInBucketHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetBucketHost<TEntity>(bucketCapacity).Create(initial);

    public static WorldEntityHost<TEntity, HashBufferStorage<TEntity>> GetHashHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world)
        where TEntity : struct
        => world.GetHost<TEntity, HashBufferStorage<TEntity>>();

    public static EntityRef<TEntity> CreateInHashHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial)
        where TEntity : struct
        => world.GetHashHost<TEntity>().Create(initial);

    public static WorldEntityHost<TEntity, ArrayBufferStorage<TEntity>> GetArrayHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int initialCapacity = 0)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, ArrayBufferStorage<TEntity>>>(out var host)
            ? host : world.GetHost<TEntity, ArrayBufferStorage<TEntity>>(() => new(initialCapacity));

    public static EntityRef<TEntity> CreateInArrayHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int initialCapacity = 0)
        where TEntity : struct
        => world.GetArrayHost<TEntity>(initialCapacity).Create(initial);

    public static WorldEntityHost<TEntity, SparseBufferStorage<TEntity>> GetSparseHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int pageSize = 256)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, SparseBufferStorage<TEntity>>>(out var host)
            ? host : world.GetHost<TEntity, SparseBufferStorage<TEntity>>(() => new(pageSize));

    public static EntityRef<TEntity> CreateInSparseHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int pageSize = 256)
        where TEntity : struct
        => world.GetSparseHost<TEntity>(pageSize).Create(initial);
    
    // unversioned buffer storages

    public static WorldEntityHost<TEntity, UnversionedBucketBufferStorage<TEntity>> GetUnversionedBucketHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetHost<TEntity, UnversionedBucketBufferStorage<TEntity>>(() => new(bucketCapacity));

    public static EntityRef<TEntity> CreateUnversionedInBucketHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetUnversionedBucketHost<TEntity>(bucketCapacity).Create(initial);

    public static WorldEntityHost<TEntity, UnversionedHashBufferStorage<TEntity>> GetUnversionedHashHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world)
        where TEntity : struct
        => world.GetHost<TEntity, UnversionedHashBufferStorage<TEntity>>();

    public static EntityRef<TEntity> CreateInUnversionedHashHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial)
        where TEntity : struct
        => world.GetUnversionedHashHost<TEntity>().Create(initial);

    public static WorldEntityHost<TEntity, UnversionedArrayBufferStorage<TEntity>> GetUnversionedArrayHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int initialCapacity = 0)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, UnversionedArrayBufferStorage<TEntity>>>(out var host)
            ? host : world.GetHost<TEntity, UnversionedArrayBufferStorage<TEntity>>(() => new(initialCapacity));

    public static EntityRef<TEntity> CreateInUnversionedArrayHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int initialCapacity = 0)
        where TEntity : struct
        => world.GetUnversionedArrayHost<TEntity>(initialCapacity).Create(initial);

    public static WorldEntityHost<TEntity, UnversionedSparseBufferStorage<TEntity>> GetUnversionedSparseHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int pageSize = 256)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, UnversionedSparseBufferStorage<TEntity>>>(out var host)
            ? host : world.GetHost<TEntity, UnversionedSparseBufferStorage<TEntity>>(() => new(pageSize));

    public static EntityRef<TEntity> CreateInUnversionedSparseHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int pageSize = 256)
        where TEntity : struct
        => world.GetUnversionedSparseHost<TEntity>(pageSize).Create(initial);
}