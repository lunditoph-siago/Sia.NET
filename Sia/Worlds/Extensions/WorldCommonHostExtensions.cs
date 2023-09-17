namespace Sia;

public static class WorldCommonHostExtensions
{
    public static WorldEntityHost<TEntity, HashBufferStorage<TEntity>> GetHashHost<TEntity>(this World world)
        where TEntity : struct
        => world.GetHost<TEntity, HashBufferStorage<TEntity>>();

    public static WorldEntityHost<TEntity, ArrayBufferStorage<TEntity>> GetArrayHost<TEntity>(this World world, int capacity)
        where TEntity : struct
        => world.GetHost<TEntity, ArrayBufferStorage<TEntity>>(() => new(capacity));

    public static WorldEntityHost<TEntity, SparseBufferStorage<TEntity>> GetSparseHost<TEntity>(this World world, int capacity = 65535, int pageSize = 256)
        where TEntity : struct
        => world.GetHost<TEntity, SparseBufferStorage<TEntity>>(() => new(capacity, pageSize));

    public static WorldEntityHost<TEntity, ManagedHeapStorage<TEntity>> GetManagedHeapHost<TEntity>(this World world)
        where TEntity : struct
        => world.GetHost<TEntity, ManagedHeapStorage<TEntity>>(() => ManagedHeapStorage<TEntity>.Instance);

    public static WorldEntityHost<TEntity, UnmanagedHeapStorage<TEntity>> GetUnmanagedHeapHost<TEntity>(this World world)
        where TEntity : struct
        => world.GetHost<TEntity, UnmanagedHeapStorage<TEntity>>(() => UnmanagedHeapStorage<TEntity>.Instance);
}