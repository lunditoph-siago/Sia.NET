namespace Sia;

public interface IEntityHost : IEnumerable<EntityRef>, IDisposable
{
    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get;}

    bool ContainsCommon<TComponent>();
    bool ContainsCommon(Type componentType);

    EntityRef Create();
    void Release(int slot, int version);
    bool IsValid(int slot, int version);

    bool Contains<TComponent>(int slot, int version);
    bool Contains(int slot, int version, Type componentType);

    ref TComponent Get<TComponent>(int slot, int version);
    ref TComponent GetOrNullRef<TComponent>(int slot, int version);

    EntityDescriptor GetDescriptor(int slot, int version);
    object Box(int slot, int version);
    Span<byte> GetSpan(int slot, int version);
}

public interface IReactiveEntityHost : IEntityHost
{
    event EntityHandler? OnEntityCreated;
    event EntityHandler? OnEntityReleased;
}

public interface IEntityHost<T> : IEntityHost
    where T : struct
{
    new EntityRef<T> Create();
    EntityRef<T> Create(in T initial);
}