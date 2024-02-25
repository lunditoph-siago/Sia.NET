namespace Sia;

public record struct AggregationGroup<TId> : IAggregationEntity<TId>
    where TId : notnull
{
    public Aggregation<TId> Aggregation { get; set; }

    public static EntityRef Create(World world)
        => world.GetHashHost<AggregationGroup<TId>>().Create();
}