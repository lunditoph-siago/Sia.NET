namespace Sia;

public static class WorldAggregatorExtensions
{
    public static Aggregation<TId>? FindAggregation<TEntity, TId>(this World world, in TId id)
        where TEntity : IAggregationEntity<TId>
        where TId : notnull, IEquatable<TId>
        => world.TryGetAddon<Aggregator<TEntity, TId>>(out var aggregator) ? aggregator.Find(id) : null;
}