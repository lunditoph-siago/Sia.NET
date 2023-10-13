namespace Sia;

using System.Collections;

public static class Aggregation
{
    public readonly record struct EntityAdded(EntityRef Entity) : IEvent;
    public readonly record struct EntityRemoved(EntityRef Entity) : IEvent;
}

public readonly record struct Aggregation<TId> : IEnumerable<EntityRef>, IDisposable
    where TId : IEquatable<TId>
{
    public EntityRef Entity { get; }
    public TId Id { get; }
    public IReadOnlySet<EntityRef> Group => _group;

    internal readonly HashSet<EntityRef> _group;

    internal Aggregation(in EntityRef entity, in TId id, HashSet<EntityRef> group)
    {
        Entity = entity;
        Id = id;
        _group = group;
    }

    public HashSet<EntityRef>.Enumerator GetEnumerator()
        => _group.GetEnumerator();

    IEnumerator<EntityRef> IEnumerable<EntityRef>.GetEnumerator()
        => _group.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => _group.GetEnumerator();

    public void Dispose()
    {
        Entity.Dispose();
    }
}