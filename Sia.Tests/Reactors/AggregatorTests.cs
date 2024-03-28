namespace Sia.Tests.Reactors;

using Sia.Reactors;

[TestCaseOrderer("Sia.Tests.PriorityOrderer", "Sia.Tests")]
public class AggregatorTests(AggregatorTests.AggregatorContext context) : IClassFixture<AggregatorTests.AggregatorContext>
{
    public class AggregatorContext : IDisposable
    {
        public readonly record struct ObjectId(int Value);

        public List<EntityRef> EntityRefs = [];

        public Aggregator<ObjectId> Aggregator;

        public World World;

        public AggregatorContext()
        {
            World = new World();
            Context<World>.Current = World;

            Aggregator = World.AcquireAddon<Aggregator<ObjectId>>();
        }

        public void Dispose() => World.Dispose();
    }

    public static List<object[]> AggregatorTestData =>
    [
        [new AggregatorContext.ObjectId[] { new(0), new(1) }, 2],
        [new AggregatorContext.ObjectId[] { new(2), new(3) }, 4],
    ];

    [Theory, Priority(0)]
    [MemberData(nameof(AggregatorTestData))]
    public void Aggregator_Setup_Test(AggregatorContext.ObjectId[] objectIds, int entityCount)
    {
        // Act
        var entityRefs = objectIds
            .Select(objectId => context.World.CreateInArrayHost(HList.Create(Sid.From(objectId))))
            .ToArray();
        context.EntityRefs.AddRange(entityRefs);

        // Assert
        Assert.Equal(objectIds.Length, entityRefs.Count());
        Assert.Equal(entityCount, context.World.Query(Matchers.Of<Aggregation<AggregatorContext.ObjectId>>()).Count);
    }

    [Theory, Priority(1)]
    [InlineData(0, 4)]
    public void Aggregator_SetSid_Test(int target, int value)
    {
        // Act
        context.EntityRefs[target].SetSid(new AggregatorContext.ObjectId(value));

        // Assert
        //TODO: Fix this one, assert will give later.
    }

    [Theory, Priority(2)]
    [InlineData(1, 7)]
    public void Aggregator_Dispose_Test(int target, int entityCount)
    {
        // Act
        var result = context.Aggregator.TryGet(new AggregatorContext.ObjectId(target), out var aggregatorEntity);
        aggregatorEntity.Dispose();

        // Assert
        Assert.True(result);
        Assert.Equal(context.World.Count, entityCount);
    }
}