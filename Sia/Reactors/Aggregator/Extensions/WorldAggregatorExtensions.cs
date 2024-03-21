namespace Sia;

public static class WorldAggregatorExtensions
{
    public static Aggregation<TId>? FindAggregation<TId>(this World world, in TId id)
        where TId : notnull, IEquatable<TId>
        => world.TryGetAddon<AggregatorBase<TId>>(out var aggregator) ? aggregator.Find(id) : null;
}