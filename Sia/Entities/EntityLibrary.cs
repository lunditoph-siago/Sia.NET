namespace Sia;

public class EntityLibrary<TEntity>
    where TEntity : struct
{
    public static EntityRef ManagedHeap(World<EntityRef> world)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.ManagedHeapFactory.Create();
        world.Add(entity);
        return entity;
    }

    public static EntityRef ManagedHeap(World<EntityRef> world, in TEntity initial)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.ManagedHeapFactory.Create(initial);
        world.Add(entity);
        return entity;
    }

    public static EntityRef UnmanagedHeap(World<EntityRef> world)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.UnmanagedHeapFactory.Create();
        world.Add(entity);
        return entity;
    }

    public static EntityRef UnmanagedHeap(World<EntityRef> world, in TEntity initial)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.UnmanagedHeapFactory.Create(initial);
        world.Add(entity);
        return entity;
    }

    public static EntityRef Hash(World<EntityRef> world)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.HashFactory.Create();
        world.Add(entity);
        return entity;
    }

    public static EntityRef Hash(World<EntityRef> world, in TEntity initial)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.HashFactory.Create(initial);
        world.Add(entity);
        return entity;
    }

    public static EntityRef Sparse(World<EntityRef> world)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.SparseFactory.Create();
        world.Add(entity);
        return entity;
    }

    public static EntityRef Sparse(World<EntityRef> world, in TEntity initial)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.SparseFactory.Create(initial);
        world.Add(entity);
        return entity;
    }

    public static EntityRef VariableSparse(World<EntityRef> world)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.VariableSparseFactory.Create();
        world.Add(entity);
        return entity;
    }

    public static EntityRef VariableSparse(World<EntityRef> world, in TEntity initial)
    {
        var lib = world.AcquireAddon<EntityLibrary<TEntity>>();
        var entity = lib.VariableSparseFactory.Create(initial);
        world.Add(entity);
        return entity;
    }

    public EntityFactory<TEntity, ManagedHeapStorage<TEntity>> ManagedHeapFactory => _managedHeapFactory.Value!;
    public EntityFactory<TEntity, UnmanagedHeapStorage<TEntity>> UnmanagedHeapFactory => _unmanagedHeapFactory.Value!;
    public EntityFactory<TEntity, HashBufferStorage<TEntity>> HashFactory => _hashStorage.Value!;
    public EntityFactory<TEntity, SparseBufferStorage<TEntity>> SparseFactory => _sparseStorage.Value!;
    public EntityFactory<TEntity, VariableStorage<TEntity, SparseBufferStorage<TEntity>>> VariableSparseFactory => _variableSparseStorage.Value!;

    private readonly Lazy<EntityFactory<TEntity, ManagedHeapStorage<TEntity>>> _managedHeapFactory
        = new(() => new(ManagedHeapStorage<TEntity>.Instance), false);
    private readonly Lazy<EntityFactory<TEntity, UnmanagedHeapStorage<TEntity>>> _unmanagedHeapFactory
        = new(() => new(UnmanagedHeapStorage<TEntity>.Instance), false);
    private readonly Lazy<EntityFactory<TEntity, HashBufferStorage<TEntity>>> _hashStorage
        = new(() => new(new()), false);
    private readonly Lazy<EntityFactory<TEntity, SparseBufferStorage<TEntity>>> _sparseStorage
        = new(() => new(new()), false);
    private readonly Lazy<EntityFactory<TEntity, VariableStorage<TEntity, SparseBufferStorage<TEntity>>>> _variableSparseStorage
        = new(() => new(new(() => new())), false);
}
