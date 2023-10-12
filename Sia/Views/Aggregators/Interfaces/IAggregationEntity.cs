namespace Sia;

public interface IAggregationEntity<TId>
    where TId : IEquatable<TId>
{
    static abstract EntityRef Create(World world);

    Aggregation<TId> Aggregation { get; set; }
}