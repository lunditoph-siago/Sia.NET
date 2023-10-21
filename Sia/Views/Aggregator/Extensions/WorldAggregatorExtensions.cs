namespace Sia;

public static class WorldAggregatorExtensions
{
    public static bool TryGetAggregation<TEntity, TKey>(this World world, in TKey key, out Aggregation<TKey> aggregation)
        where TEntity : IAggregationEntity<TKey>
        where TKey : notnull
    {
        if (!world.TryGetAddon<Aggregator<TEntity, TKey>>(out var aggregator)) {
            aggregation = default;
            return false;
        }
        return aggregator.TryGet(key, out aggregation);
    }
}