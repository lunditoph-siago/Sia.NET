namespace Sia;

using System.Collections;

public static class Aggregation
{
    public readonly record struct EntityAdded(EntityRef Entity) : IEvent;
    public readonly record struct EntityRemoved(EntityRef Entity) : IEvent;
}

public record struct Aggregation<TId> : IEnumerable<EntityRef>, IDisposable
    where TId : notnull
{
    public TId Id { get; }
    public EntityRef First { get; internal set; }
    public readonly IReadOnlySet<EntityRef> Group => RawGroup;
    
    internal EntityRef Entity { get; }
    internal HashSet<EntityRef> RawGroup { get; }

    internal Aggregation(in EntityRef entity, in TId id, HashSet<EntityRef> group)
    {
        Entity = entity;
        Id = id;
        RawGroup = group;
    }

    public readonly HashSet<EntityRef>.Enumerator GetEnumerator()
        => RawGroup.GetEnumerator();

    readonly IEnumerator<EntityRef> IEnumerable<EntityRef>.GetEnumerator()
        => RawGroup.GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator()
        => RawGroup.GetEnumerator();

    public void Dispose()
    {
        Entity.Dispose();
    }
}