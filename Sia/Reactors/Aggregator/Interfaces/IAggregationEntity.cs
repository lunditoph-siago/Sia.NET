namespace Sia;

public interface IAggregationEntity<TId>
    where TId : notnull
{
    static abstract EntityRef Create(World world);

    Aggregation<TId> Aggregation { get; set; }
}