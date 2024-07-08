namespace Sia.Tests.Reactors;

using Sia.Reactors;

[TestCaseOrderer("Sia.Tests.PriorityOrderer", "Sia.Tests")]
public class MapperTests(MapperTests.MapperContext context) : IClassFixture<MapperTests.MapperContext>
{
    public class MapperContext : IDisposable
    {
        public readonly record struct ObjectId(Guid Value);

        public List<Entity> Entities = [];

        public Mapper<ObjectId> Mapper;

        public World World;

        public MapperContext()
        {
            World = new World();
            Context<World>.Current = World;

            Mapper = World.AcquireAddon<Mapper<ObjectId>>();
        }

        public void Dispose() => World.Dispose();
    }

    public static List<object[]> MapperTestData =>
    [
        [new MapperContext.ObjectId[] { new(Guid.NewGuid()), new(Guid.NewGuid()) }],
    ];

    [Theory, Priority(0)]
    [MemberData(nameof(MapperTestData))]
    public void Mapper_Setup_Test(MapperContext.ObjectId[] objectIds)
    {
        foreach (var objectId in objectIds) {
            // Act
            var entityRef = context.World.Create(HList.From(Sid.From(objectId)));
            context.Entities.Add(entityRef);

            // Assert
            Assert.True(entityRef == context.Mapper[objectId]);
        }
    }

    [Theory, Priority(1)]
    [InlineData(0)]
    public void Mapper_SetSid_Test(int target)
    {
        // Act
        var id = new MapperContext.ObjectId(Guid.NewGuid());
        context.Entities[target].SetSid(id);

        // Assert
        Assert.True(context.Mapper[id] == context.Entities[target]);
    }
}