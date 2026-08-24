namespace Sia.Tests.Reactors;

using Sia.Reactors;

public class AggregatorTests
{
    private readonly record struct ObjectId(int Value);

    [Fact]
    public void EntitiesSharingASid_MergeIntoOneAggregation()
    {
        using var world = new World();
        world.AcquireAddon<Aggregator<ObjectId>>();

        world.Create(HList.From(Sid.From(new ObjectId(0))));
        world.Create(HList.From(Sid.From(new ObjectId(0))));

        Assert.Equal(1, world.Query(Matchers.Of<Aggregation<ObjectId>>()).Count);
    }

    [Fact]
    public void EntitiesWithDistinctSids_EachGetTheirOwnAggregation()
    {
        using var world = new World();
        world.AcquireAddon<Aggregator<ObjectId>>();

        world.Create(HList.From(Sid.From(new ObjectId(0))));
        world.Create(HList.From(Sid.From(new ObjectId(1))));

        Assert.Equal(2, world.Query(Matchers.Of<Aggregation<ObjectId>>()).Count);
    }

    [Fact]
    public void SettingSid_MovesTheEntityIntoTheNewAggregation()
    {
        using var world = new World();
        var aggregator = world.AcquireAddon<Aggregator<ObjectId>>();
        var entity = world.Create(HList.From(Sid.From(new ObjectId(0))));
        world.Create(HList.From(Sid.From(new ObjectId(0))));

        var newId = new ObjectId(2);
        entity.SetSid(newId);

        Assert.True(aggregator.TryGet(newId, out var aggregationEntity));
        Assert.Equal(entity, aggregationEntity.Get<Aggregation<ObjectId>>().First);
    }

    [Fact]
    public void SettingSidOnTheSoleAggregationMember_MovesItIntoTheNewAggregation()
    {
        using var world = new World();
        var aggregator = world.AcquireAddon<Aggregator<ObjectId>>();
        var entity = world.Create(HList.From(Sid.From(new ObjectId(0))));

        entity.SetSid(new ObjectId(2));

        Assert.True(aggregator.TryGet(new ObjectId(2), out var aggregationEntity));
        Assert.Equal(entity, aggregationEntity.Get<Aggregation<ObjectId>>().First);
        Assert.False(aggregator.TryGet(new ObjectId(0), out _));
        Assert.Equal(1, world.Query(Matchers.Of<Aggregation<ObjectId>>()).Count);
    }

    [Fact]
    public void DestroyingTheAggregationEntity_DoesNotDestroyItsMembers()
    {
        using var world = new World();
        var aggregator = world.AcquireAddon<Aggregator<ObjectId>>();
        var member = world.Create(HList.From(Sid.From(new ObjectId(1))));

        Assert.True(aggregator.TryGet(new ObjectId(1), out var aggregationEntity));
        aggregationEntity.Destroy();

        Assert.True(member.IsValid);
    }
}
