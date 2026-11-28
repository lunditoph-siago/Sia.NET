using System.Runtime.CompilerServices;

namespace Sia;

public static class WorldCommonHostExtensions
{
    #region ArrayHost

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> GetArrayHost<TEntity>(this World world)
        where TEntity : struct, IHList
        => world.AcquireArrayHost<TEntity>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Entity Create(this World world)
        => world.GetArrayHost<EmptyHList>().Create();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Entity Create<TEntity>(this World world, in TEntity initial)
        where TEntity : struct, IHList
        => world.GetArrayHost<TEntity>().Create(initial);

    /// <summary>Creates entities with the same initial value.</summary>
    /// <param name="world">The world that owns the entities.</param>
    /// <param name="count">The number of entities to create.</param>
    /// <param name="initial">The initial value for each entity.</param>
    /// <param name="runner">An optional worker pool for a large batch in a fresh world.</param>
    public static void CreateMany<TEntity>(this World world, int count, in TEntity initial, ParallelRunner? runner = null)
        where TEntity : struct, IHList
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count != 0) world.GetArrayHost<TEntity>().CreateMany(count, initial, runner);
    }

    #endregion // ArrayHost

    #region UnmanagedArrayHost

    public static WorldEntityHost<TEntity, UnmanagedArrayEntityHost<TEntity>> GetUnmanagedArrayHost<TEntity>(this World world)
        where TEntity : struct, IHList
        => world.AcquireHost<TEntity, UnmanagedArrayEntityHost<TEntity>>();

    public static Entity CreateUnmanaged(this World world)
        => world.GetUnmanagedArrayHost<EmptyHList>().Create();

    public static Entity CreateUnmanaged<TEntity>(this World world, in TEntity initial)
        where TEntity : struct, IHList
        => world.GetUnmanagedArrayHost<TEntity>().Create(initial);

    #endregion // ArrayHost
}
