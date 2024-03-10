using System.Diagnostics.CodeAnalysis;

namespace Sia;

public static class WorldCommonHostExtensions
{
    // buffer storages

    public static WorldEntityHost<TEntity, BucketBufferStorage<WithId<TEntity>>> GetBucketHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetHost<TEntity, BucketBufferStorage<WithId<TEntity>>>(() => new(bucketCapacity));

    public static EntityRef<WithId<TEntity>> CreateInBucketHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetBucketHost<TEntity>(bucketCapacity).Create(initial);

    public static WorldEntityHost<TEntity, HashBufferStorage<WithId<TEntity>>> GetHashHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world)
        where TEntity : struct
        => world.GetHost<TEntity, HashBufferStorage<WithId<TEntity>>>();

    public static EntityRef<WithId<TEntity>> CreateInHashHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial)
        where TEntity : struct
        => world.GetHashHost<TEntity>().Create(initial);

    public static WorldEntityHost<TEntity, ArrayBufferStorage<WithId<TEntity>>> GetArrayHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int initialCapacity = 0)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, ArrayBufferStorage<WithId<TEntity>>>>(out var host)
            ? host : world.GetHost<TEntity, ArrayBufferStorage<WithId<TEntity>>>(() => new(initialCapacity));

    public static EntityRef<WithId<TEntity>> CreateInArrayHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int initialCapacity = 0)
        where TEntity : struct
        => world.GetArrayHost<TEntity>(initialCapacity).Create(initial);

    public static WorldEntityHost<TEntity, SparseBufferStorage<WithId<TEntity>>> GetSparseHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int pageSize = 256)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, SparseBufferStorage<WithId<TEntity>>>>(out var host)
            ? host : world.GetHost<TEntity, SparseBufferStorage<WithId<TEntity>>>(() => new(pageSize));

    public static EntityRef<WithId<TEntity>> CreateInSparseHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int pageSize = 256)
        where TEntity : struct
        => world.GetSparseHost<TEntity>(pageSize).Create(initial);
    
    // unversioned buffer storages

    public static WorldEntityHost<TEntity, UnversionedBucketBufferStorage<WithId<TEntity>>> GetUnversionedBucketHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetHost<TEntity, UnversionedBucketBufferStorage<WithId<TEntity>>>(() => new(bucketCapacity));

    public static EntityRef<WithId<TEntity>> CreateUnversionedInBucketHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int bucketCapacity = 256)
        where TEntity : struct
        => world.GetUnversionedBucketHost<TEntity>(bucketCapacity).Create(initial);

    public static WorldEntityHost<TEntity, UnversionedHashBufferStorage<WithId<TEntity>>> GetUnversionedHashHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world)
        where TEntity : struct
        => world.GetHost<TEntity, UnversionedHashBufferStorage<WithId<TEntity>>>();

    public static EntityRef<WithId<TEntity>> CreateInUnversionedHashHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial)
        where TEntity : struct
        => world.GetUnversionedHashHost<TEntity>().Create(initial);

    public static WorldEntityHost<TEntity, UnversionedArrayBufferStorage<WithId<TEntity>>> GetUnversionedArrayHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int initialCapacity = 0)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, UnversionedArrayBufferStorage<WithId<TEntity>>>>(out var host)
            ? host : world.GetHost<TEntity, UnversionedArrayBufferStorage<WithId<TEntity>>>(() => new(initialCapacity));

    public static EntityRef<WithId<TEntity>> CreateInUnversionedArrayHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int initialCapacity = 0)
        where TEntity : struct
        => world.GetUnversionedArrayHost<TEntity>(initialCapacity).Create(initial);

    public static WorldEntityHost<TEntity, UnversionedSparseBufferStorage<WithId<TEntity>>> GetUnversionedSparseHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, int pageSize = 256)
        where TEntity : struct
        => world.TryGetHost<WorldEntityHost<TEntity, UnversionedSparseBufferStorage<WithId<TEntity>>>>(out var host)
            ? host : world.GetHost<TEntity, UnversionedSparseBufferStorage<WithId<TEntity>>>(() => new(pageSize));

    public static EntityRef<WithId<TEntity>> CreateInUnversionedSparseHost<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
            this World world, in TEntity initial, int pageSize = 256)
        where TEntity : struct
        => world.GetUnversionedSparseHost<TEntity>(pageSize).Create(initial);
}