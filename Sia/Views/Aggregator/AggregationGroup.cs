namespace Sia;

public record struct AggregationGroup<TId> : IAggregationEntity<TId>
    where TId : IEquatable<TId>
{
    public Aggregation<TId> Aggregation { get; set; }

    public static EntityRef Create(World world)
        => world.GetHashHost<AggregationGroup<TId>>().Create();
}