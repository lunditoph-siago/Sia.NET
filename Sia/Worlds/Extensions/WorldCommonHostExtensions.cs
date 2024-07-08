namespace Sia;

public static class WorldCommonHostExtensions
{
    #region ArrayHost

    public static WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> GetArrayHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, ArrayEntityHost<TEntity>>();

    public static Entity Create(this World world)
        => world.GetArrayHost<EmptyHList>().Create();

    public static Entity Create<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetArrayHost<TEntity>().Create(initial);
    
    #endregion // ArrayHost

    #region UnmanagedArrayHost

    public static WorldEntityHost<TEntity, UnmanagedArrayEntityHost<TEntity>> GetUnmanagedArrayHost<TEntity>(this World world)
        where TEntity : IHList
        => world.AcquireHost<TEntity, UnmanagedArrayEntityHost<TEntity>>();

    public static Entity CreateUnmanaged(this World world)
        => world.GetUnmanagedArrayHost<EmptyHList>().Create();

    public static Entity CreateUnmanaged<TEntity>(this World world, in TEntity initial)
        where TEntity : IHList
        => world.GetUnmanagedArrayHost<TEntity>().Create(initial);
    
    #endregion // ArrayHost
}