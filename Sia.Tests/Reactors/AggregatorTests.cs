namespace Sia.Tests.Reactors;

using Sia.Reactors;

[TestCaseOrderer("Sia.Tests.PriorityOrderer", "Sia.Tests")]
public class AggregatorTests(AggregatorTests.AggregatorContext context) : IClassFixture<AggregatorTests.AggregatorContext>
{
    public class AggregatorContext : IDisposable
    {
        public readonly record struct ObjectId(int Value);

        public List<Entity> Entities = [];

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
        [new AggregatorContext.ObjectId[] { new(0), new(0) }, 1],
        [new AggregatorContext.ObjectId[] { new(1), new(1) }, 2],
    ];

    [Theory, Priority(0)]
    [MemberData(nameof(AggregatorTestData))]
    public void Aggregator_Setup_Test(AggregatorContext.ObjectId[] objectIds, int entityCount)
    {
        // Act
        var entityRefs = objectIds
            .Select(objectId => context.World.Create(HList.Create(Sid.From(objectId))))
            .ToArray();
        context.Entities.AddRange(entityRefs);

        // Assert
        Assert.Equal(objectIds.Length, entityRefs.Count());
        Assert.Equal(entityCount, context.World.Query(Matchers.Of<Aggregation<AggregatorContext.ObjectId>>()).Count);
    }

    [Theory, Priority(1)]
    [InlineData(0, 2)]
    public void Aggregator_SetSid_Test(int target, int value)
    {
        // Arrange
        var objectId = new AggregatorContext.ObjectId(value);

        // Act
        context.Entities[target].SetSid(objectId);

        // Assert
        var actualResult = new List<AggregatorContext.ObjectId>();
        foreach (var entity in context.World.Query(Matchers.Of<Aggregation<AggregatorContext.ObjectId>>())) {
            ref var aggregation = ref entity.Get<Aggregation<AggregatorContext.ObjectId>>();
            actualResult.Add(aggregation.Id);
        }
        Assert.Contains(objectId, actualResult);
    }

    [Theory, Priority(2)]
    [InlineData(1, 6)]
    public void Aggregator_Dispose_Test(int target, int entityCount)
    {
        // Act
        var result = context.Aggregator.TryGet(new AggregatorContext.ObjectId(target), out var aggregatorEntity);
        aggregatorEntity?.Dispose();

        // Assert
        Assert.True(result);
        Assert.Equal(entityCount, context.World.Count);
    }
}