namespace Sia;

public interface IEntityHost : IEnumerable<EntityRef>
{
    EntityDescriptor Descriptor { get; }

    int Capacity { get; }
    int Count { get; }

    EntityRef Create();
    void Release(nint pointer, int version);

    bool Contains<TComponent>(nint pointer, int version);
    bool Contains(nint pointer, int version, Type type);

    ref TComponent Get<TComponent>(nint pointer, int version);
    ref TComponent GetOrNullRef<TComponent>(nint pointer, int version);

    void IterateAllocated(StoragePointerHandler handler);
    void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler);

    object Box(nint pointer, int version);
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