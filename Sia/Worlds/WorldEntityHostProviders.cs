namespace Sia;

public static class WorldEntityHostProviders
{
    public sealed class Bucket(World world, int bucketCapacity = 256) : IEntityHostProvider
    {
        public IEntityHost<TEntity> GetHost<TEntity>()
            where TEntity : IHList
            => world.GetBucketHost<TEntity>(bucketCapacity);
    }

    public sealed class UnversionedBucket(World world, int bucketCapacity = 256) : IEntityHostProvider
    {
        public IEntityHost<TEntity> GetHost<TEntity>()
            where TEntity : IHList
            => world.GetUnversionedBucketHost<TEntity>(bucketCapacity);
    }

    public sealed class Hash(World world) : IEntityHostProvider
    {
        public IEntityHost<TEntity> GetHost<TEntity>()
            where TEntity : IHList
            => world.GetHashHost<TEntity>();
    }

    public sealed class UnversionedHash(World world) : IEntityHostProvider
    {
        public IEntityHost<TEntity> GetHost<TEntity>()
            where TEntity : IHList
            => world.GetUnversionedHashHost<TEntity>();
    }

    public sealed class Array(World world, int initialCapacity = 0) : IEntityHostProvider
    {
        public IEntityHost<TEntity> GetHost<TEntity>()
            where TEntity : IHList
            => world.GetArrayHost<TEntity>(initialCapacity);
    }

    public sealed class UnversionedArray(World world, int initialCapacity = 0) : IEntityHostProvider
    {
        public IEntityHost<TEntity> GetHost<TEntity>()
            where TEntity : IHList
            => world.GetUnversionedArrayHost<TEntity>(initialCapacity);
    }

    public sealed class Sparse(World world, int pageSize = 256) : IEntityHostProvider
    {
        public IEntityHost<TEntity> GetHost<TEntity>()
            where TEntity : IHList
            => world.GetSparseHost<TEntity>(pageSize);
    }

    public sealed class UnversionedSparse(World world, int pageSize = 256) : IEntityHostProvider
    {
        public IEntityHost<TEntity> GetHost<TEntity>()
            where TEntity : IHList
            => world.GetUnversionedSparseHost<TEntity>(pageSize);
    }
}