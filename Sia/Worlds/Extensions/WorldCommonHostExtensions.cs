namespace Sia;

public static class WorldCommonHostExtensions
{
    // buffer storages

    #region BucketHost

    public static WorldEntityHost<TEntity, BucketBufferStorage<HList<Entity, TEntity>>> GetBucketHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, BucketBufferStorage<HList<Entity, TEntity>>>();

    public static Entity CreateInBucketHost(this World world)
        => world.GetBucketHost<EmptyHList>().Create();

    public static Entity CreateInBucketHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetBucketHost<TEntity>().Create(initial);
    
    #endregion // BucketHost

    #region HashHost

    public static WorldEntityHost<TEntity, HashBufferStorage<HList<Entity, TEntity>>> GetHashHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, HashBufferStorage<HList<Entity, TEntity>>>();

    public static Entity CreateInHashHost(this World world)
        => world.GetHashHost<EmptyHList>().Create();

    public static Entity CreateInHashHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetHashHost<TEntity>().Create(initial);
    
    #endregion // HashHost

    #region ArrayHost

    public static WorldEntityHost<TEntity, ArrayBufferStorage<HList<Entity, TEntity>>> GetArrayHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, ArrayBufferStorage<HList<Entity, TEntity>>>();

    public static Entity CreateInArrayHost(this World world)
        => world.GetArrayHost<EmptyHList>().Create();

    public static Entity CreateInArrayHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetArrayHost<TEntity>().Create(initial);
    
    #endregion // ArrayHost

    #region SparseHost

    public static WorldEntityHost<TEntity, SparseBufferStorage<HList<Entity, TEntity>>> GetSparseHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, SparseBufferStorage<HList<Entity, TEntity>>>();

    public static Entity CreateInSparseHost(this World world)
        => world.GetSparseHost<EmptyHList>().Create();

    public static Entity CreateInSparseHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetSparseHost<TEntity>().Create(initial);
    
    #endregion // SparseHost
    
    // unversioned buffer storages

    #region UnversionedBucketHost

    public static WorldEntityHost<TEntity, UnversionedBucketBufferStorage<HList<Entity, TEntity>>> GetUnversionedBucketHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnversionedBucketBufferStorage<HList<Entity, TEntity>>>();

    public static Entity CreateInUnversionedBucketHost(this World world)
        => world.GetUnversionedBucketHost<EmptyHList>().Create();

    public static Entity CreateInUnversionedBucketHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnversionedBucketHost<TEntity>().Create(initial);
    
    #endregion // UnversionedBucketHost

    #region UnversionedHashHost

    public static WorldEntityHost<TEntity, UnversionedHashBufferStorage<HList<Entity, TEntity>>> GetUnversionedHashHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnversionedHashBufferStorage<HList<Entity, TEntity>>>();

    public static Entity CreateInUnversionedHashHost(this World world)
        => world.GetUnversionedHashHost<EmptyHList>().Create();

    public static Entity CreateInUnversionedHashHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnversionedHashHost<TEntity>().Create(initial);
    
    #endregion // UnversionedHashHost

    #region UnversionedArrayHost

    public static WorldEntityHost<TEntity, UnversionedArrayBufferStorage<HList<Entity, TEntity>>> GetUnversionedArrayHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnversionedArrayBufferStorage<HList<Entity, TEntity>>>();

    public static Entity CreateInUnversionedArrayHost(this World world)
        => world.GetUnversionedArrayHost<EmptyHList>().Create();

    public static Entity CreateInUnversionedArrayHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnversionedArrayHost<TEntity>().Create(initial);
    
    #endregion // UnversionedArrayHost

    #region UnversionedSparseHost

    public static WorldEntityHost<TEntity, UnversionedSparseBufferStorage<HList<Entity, TEntity>>> GetUnversionedSparseHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnversionedSparseBufferStorage<HList<Entity, TEntity>>>();

    public static Entity CreateInUnversionedSparseHost(this World world)
        => world.GetUnversionedSparseHost<EmptyHList>().Create();

    public static Entity CreateInUnversionedSparseHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnversionedSparseHost<TEntity>().Create(initial);
    
    #endregion // UnversionedSparseHost
}