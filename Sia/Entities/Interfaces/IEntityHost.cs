namespace Sia;

public interface IEntityHost : IEnumerable<EntityRef>
{
    EntityDescriptor Descriptor { get; }

    int Capacity { get; }
    int Count { get; }

    EntityRef Create();
    void Release(long pointer);

    bool Contains<TComponent>(long pointer);
    bool Contains(long pointer, Type type);

    ref TComponent Get<TComponent>(long pointer);
    ref TComponent GetOrNullRef<TComponent>(long pointer);

    void IterateAllocated(StoragePointerHandler handler);
    void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler);

    object Box(long pointer);
}

public interface IReactiveEntityHost : IEntityHost
{
    event EntityHandler? OnEntityCreated;
    event EntityHandler? OnEntityReleased;
}

public interface IEntityHost<T> : IEntityHost
    where T : struct
{
    EntityRef Create(in T initial);
}