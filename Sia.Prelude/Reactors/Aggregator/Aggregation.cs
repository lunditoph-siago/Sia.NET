namespace Sia.Reactors;

using System.Collections.Immutable;

public struct Aggregation<TId>
    where TId : notnull, IEquatable<TId>
{
    public readonly record struct EntityAdded(Entity Entity) : IEvent;
    public readonly record struct EntityRemoved(Entity Entity) : IEvent;

    public AggregatorBase<TId>? Aggregator { get; internal set; }
    public TId Id { get; internal set; }
    public Entity First { get; internal set; }
    public readonly IReadOnlySet<Entity> Group =>
        _group ?? (IReadOnlySet<Entity>)ImmutableHashSet<Entity>.Empty;

    public readonly int Count => _group!.Count;
    public readonly bool IsReadOnly => false;

    internal HashSet<Entity>? _group;
}