namespace Sia;

public static class WorldEntityCreators
{
    public sealed class Bucket(World world, int bucketCapacity = 256) : IEntityCreator
    {
        public EntityRef<WithId<TEntity>> CreateEntity<TEntity>(TEntity initial)
            where TEntity : struct
            => world.CreateInBucketHost(initial, bucketCapacity);
    }

    public sealed class Hash(World world) : IEntityCreator
    {
        public EntityRef<WithId<TEntity>> CreateEntity<TEntity>(TEntity initial)
            where TEntity : struct
            => world.CreateInHashHost(initial);
    }

    public sealed class Array(World world, int initialCapacity = 0) : IEntityCreator
    {
        public EntityRef<WithId<TEntity>> CreateEntity<TEntity>(TEntity initial)
            where TEntity : struct
            => world.CreateInArrayHost(initial, initialCapacity);
    }

    public sealed class Sparse(World world, int pageSize = 256) : IEntityCreator
    {
        public EntityRef<WithId<TEntity>> CreateEntity<TEntity>(TEntity initial)
            where TEntity : struct
            => world.CreateInSparseHost(initial, pageSize);
    }
}