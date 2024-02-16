using CommunityToolkit.HighPerformance.Buffers;

namespace Sia;

public interface IEntityHost : IEnumerable<EntityRef>, IDisposable
{
    event Action? OnDisposed;

    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get;}

    bool ContainsCommon<TComponent>();
    bool ContainsCommon(Type componentType);

    EntityRef Create();
    void Release(scoped in StorageSlot slot);
    bool IsValid(scoped in StorageSlot slot);

    bool Contains<TComponent>(scoped in StorageSlot slot);
    bool Contains(scoped in StorageSlot slot, Type componentType);

    ref TComponent Get<TComponent>(scoped in StorageSlot slot);
    ref TComponent GetOrNullRef<TComponent>(scoped in StorageSlot slot);

    EntityDescriptor GetDescriptor(scoped in StorageSlot slot);
    object Box(scoped in StorageSlot slot);
    Span<byte> GetSpan(scoped in StorageSlot slot);
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

    SpanOwner<T> Fetch(ReadOnlySpan<StorageSlot> slots);
    SpanOwner<T> UnsafeFetch(ReadOnlySpan<StorageSlot> slots);
    SpanOwner<T> FetchAll() => UnsafeFetch(AllocatedSlots);

    void Write(ReadOnlySpan<StorageSlot> slots, ReadOnlySpan<T> values);
    void UnsafeWrite(ReadOnlySpan<StorageSlot> slots, ReadOnlySpan<T> values);
}