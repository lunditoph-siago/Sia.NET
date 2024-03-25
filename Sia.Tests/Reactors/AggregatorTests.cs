namespace Sia.Tests.Reactors;

[TestCaseOrderer("Sia.Tests.PriorityOrderer", "Sia.Tests")]
public class AggregatorTests : IDisposable
{
    public readonly record struct ObjectId(int Value)
    {
        public static implicit operator ObjectId(int id) => new(id);
    }

    public static List<object[]> AggregatorTestData =>
    [
        [new[] { new ObjectId(0), new ObjectId(1) }],
    ];

    public static readonly List<EntityRef>? EntityRefs = [];

    public static Aggregator<ObjectId>? Aggregator;

    public static World? World;

    public AggregatorTests()
    {
        World = new World();
        Context<World>.Current = World;

        Aggregator = World.AcquireAddon<Aggregator<ObjectId>>();
    }

    [Theory, Priority(0)]
    [MemberData(nameof(AggregatorTestData))]
    public void Aggregator_Setup_Test(ObjectId[] objectIds)
    {
        // Act
        foreach (var objectId in objectIds)
        {
            var entityRef = World!.CreateInArrayHost(HList.Create(Sid.From(objectId)));
            EntityRefs?.Add(entityRef);
        }

        // Assert
        foreach (var entity in World!.Query(Matchers.Of<Aggregation<ObjectId>>()))
        {
            ref var aggregation = ref entity.Get<Aggregation<ObjectId>>();
            Assert.Equal(new ObjectId(1), aggregation.Id);
        }
    }

    [Theory, Priority(1)]
    [InlineData(2)]
    public void Aggregator_SetSid_Test(int id)
    {
        // Act
        EntityRefs?[0].SetSid(new ObjectId(id));

        // Assert
        foreach (var entity in World!.Query(Matchers.Of<Aggregation<ObjectId>>()))
        {
            ref var aggregation = ref entity.Get<Aggregation<ObjectId>>();
            Assert.Equal(new ObjectId(id), aggregation.Id);
        }
    }

    public void Dispose() => World?.Dispose();
}