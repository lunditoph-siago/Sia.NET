using CommunityToolkit.HighPerformance.Buffers;

namespace Sia;

public interface IEntityHost : IEnumerable<EntityRef>, IDisposable
{
    int Capacity { get; }
    int Count { get; }
    ReadOnlySpan<StorageSlot> AllocatedSlots { get;}

    bool ContainsCommon<TComponent>();
    bool ContainsCommon(Type componentType);

    EntityRef Create();
    void Release(StorageSlot slot);
    bool IsValid(StorageSlot slot);

    bool Contains<TComponent>(StorageSlot slot);
    bool Contains(StorageSlot slot, Type componentType);

    ref TComponent Get<TComponent>(StorageSlot slot);
    ref TComponent GetOrNullRef<TComponent>(StorageSlot slot);

    EntityDescriptor GetDescriptor(StorageSlot slot);
    object Box(StorageSlot slot);
    Span<byte> GetSpan(StorageSlot slot);
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