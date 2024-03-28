namespace Sia.Reactors;

using System.Collections.Immutable;

public struct Aggregation<TId>
    where TId : notnull, IEquatable<TId>
{
    public readonly record struct EntityAdded(EntityRef Entity) : IEvent;
    public readonly record struct EntityRemoved(EntityRef Entity) : IEvent;

    public AggregatorBase<TId>? Aggregator { get; internal set; }
    public TId Id { get; internal set; }
    public EntityRef First { get; internal set; }
    public readonly IReadOnlySet<EntityRef> Group =>
        _group ?? (IReadOnlySet<EntityRef>)ImmutableHashSet<EntityRef>.Empty;

    public readonly int Count => _group!.Count;
    public readonly bool IsReadOnly => false;

    internal HashSet<EntityRef>? _group;
}