namespace Sia;

public static class WorldCommonHostExtensions
{
    // buffer storages

    #region BucketHost

    public static WorldEntityHost<TEntity, BucketBufferStorage<HList<Identity, TEntity>>> GetBucketHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, BucketBufferStorage<HList<Identity, TEntity>>>();

    public static EntityRef CreateInBucketHost(this World world)
        => world.GetBucketHost<EmptyHList>().Create();

    public static EntityRef CreateInBucketHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetBucketHost<TEntity>().Create(initial);
    
    #endregion // BucketHost

    #region HashHost

    public static WorldEntityHost<TEntity, HashBufferStorage<HList<Identity, TEntity>>> GetHashHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, HashBufferStorage<HList<Identity, TEntity>>>();

    public static EntityRef CreateInHashHost(this World world)
        => world.GetHashHost<EmptyHList>().Create();

    public static EntityRef CreateInHashHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetHashHost<TEntity>().Create(initial);
    
    #endregion // HashHost

    #region ArrayHost

    public static WorldEntityHost<TEntity, ArrayBufferStorage<HList<Identity, TEntity>>> GetArrayHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, ArrayBufferStorage<HList<Identity, TEntity>>>();

    public static EntityRef CreateInArrayHost(this World world)
        => world.GetArrayHost<EmptyHList>().Create();

    public static EntityRef CreateInArrayHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetArrayHost<TEntity>().Create(initial);
    
    #endregion // ArrayHost

    #region SparseHost

    public static WorldEntityHost<TEntity, SparseBufferStorage<HList<Identity, TEntity>>> GetSparseHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, SparseBufferStorage<HList<Identity, TEntity>>>();

    public static EntityRef CreateInSparseHost(this World world)
        => world.GetSparseHost<EmptyHList>().Create();

    public static EntityRef CreateInSparseHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetSparseHost<TEntity>().Create(initial);
    
    #endregion // SparseHost
    
    // unversioned buffer storages

    #region UnversionedBucketHost

    public static WorldEntityHost<TEntity, UnversionedBucketBufferStorage<HList<Identity, TEntity>>> GetUnversionedBucketHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnversionedBucketBufferStorage<HList<Identity, TEntity>>>();

    public static EntityRef CreateInUnversionedBucketHost(this World world)
        => world.GetUnversionedBucketHost<EmptyHList>().Create();

    public static EntityRef CreateInUnversionedBucketHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnversionedBucketHost<TEntity>().Create(initial);
    
    #endregion // UnversionedBucketHost

    #region UnversionedHashHost

    public static WorldEntityHost<TEntity, UnversionedHashBufferStorage<HList<Identity, TEntity>>> GetUnversionedHashHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnversionedHashBufferStorage<HList<Identity, TEntity>>>();

    public static EntityRef CreateInUnversionedHashHost(this World world)
        => world.GetUnversionedHashHost<EmptyHList>().Create();

    public static EntityRef CreateInUnversionedHashHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnversionedHashHost<TEntity>().Create(initial);
    
    #endregion // UnversionedHashHost

    #region UnversionedArrayHost

    public static WorldEntityHost<TEntity, UnversionedArrayBufferStorage<HList<Identity, TEntity>>> GetUnversionedArrayHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnversionedArrayBufferStorage<HList<Identity, TEntity>>>();

    public static EntityRef CreateInUnversionedArrayHost(this World world)
        => world.GetUnversionedArrayHost<EmptyHList>().Create();

    public static EntityRef CreateInUnversionedArrayHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnversionedArrayHost<TEntity>().Create(initial);
    
    #endregion // UnversionedArrayHost

    #region UnversionedSparseHost

    public static WorldEntityHost<TEntity, UnversionedSparseBufferStorage<HList<Identity, TEntity>>> GetUnversionedSparseHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnversionedSparseBufferStorage<HList<Identity, TEntity>>>();

    public static EntityRef CreateInUnversionedSparseHost(this World world)
        => world.GetUnversionedSparseHost<EmptyHList>().Create();

    public static EntityRef CreateInUnversionedSparseHost<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnversionedSparseHost<TEntity>().Create(initial);
    
    #endregion // UnversionedSparseHost
}