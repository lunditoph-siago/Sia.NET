namespace Sia.Tests.Reactors;

[TestCaseOrderer("Sia.Tests.PriorityOrderer", "Sia.Tests")]
public class MapperTests : IDisposable
{
    public readonly record struct ObjectId(Guid Value)
    {
        public static ObjectId Create() => new(Guid.NewGuid());
    }

    public static List<object[]> MapperTestData =>
    [
        [new[] { ObjectId.Create(), ObjectId.Create() }],
    ];

    public static readonly List<EntityRef>? EntityRefs = [];

    public static Mapper<ObjectId>? Mapper;

    public static World? World;

    public MapperTests()
    {
        World = new World();
        Context<World>.Current = World;

        Mapper = World.AcquireAddon<Mapper<ObjectId>>();
    }

    [Theory, Priority(0)]
    [MemberData(nameof(MapperTestData))]
    public void Mapper_Setup_Test(ObjectId[] objectIds)
    {
        foreach (var objectId in objectIds)
        {
            // Act
            var entityRef = World!.CreateInArrayHost(HList.Create(Sid.From(objectId)));
            EntityRefs?.Add(entityRef);

            // Assert
            Assert.True(entityRef == Mapper?[objectId]);
        }
    }

    [Fact, Priority(1)]
    public void Mapper_SetSid_Test()
    {
        // Act
        var id = ObjectId.Create();
        EntityRefs?.First().SetSid(id);

        // Assert
        Assert.True(Mapper?[id] == EntityRefs?.First());
    }

    public void Dispose() => World?.Dispose();
}