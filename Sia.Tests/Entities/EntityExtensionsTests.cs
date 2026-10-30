using Sia.Reactors;

namespace Sia.Tests.Entities;

public class EntityExtensionsTests
{
    public readonly record struct ObjectId(int Value)
    {
        public static implicit operator ObjectId(int id)
            => new(id);
    }

    [Fact]
    public void EntityHostHandle_Test()
    {
        // Arrange
        var host = new ArrayEntityHost<HList<Sid<ObjectId>, EmptyHList>>();
        host.Create(HList.From(new Sid<ObjectId>(0)));

        // Act & Assert
        host.Handle((refHost, from, to) => {
            Assert.Equal(host, refHost);
            Assert.Equal(0, from);
            Assert.Equal(1, to);
        });
    }

    [Fact]
    public void EntityHostForEach_Test()
    {
        // Arrange
        var host = new ArrayEntityHost<HList<Sid<ObjectId>, EmptyHList>>();
        host.Create(HList.From(new Sid<ObjectId>(0)));

        // Act & Assert
        host.ForEach((EntityHandler)(entity => {
            ref var id = ref entity.Get<Sid<ObjectId>>();
            Assert.Equal(0, id.Value.Value);
        }));
    }

    private struct SliceSumHandler : ISliceHandler<Sid<ObjectId>>
    {
        public int Sum;

        public void Handle(ref Sid<ObjectId> id) => Sum += id.Value.Value;
    }

    [Fact]
    public void QueryForSlice_StructHandler_Test()
    {
        using var world = new World();
        for (var i = 0; i < 32; i++) {
            world.Create(HList.From(new Sid<ObjectId>(i)));
        }
        var query = world.Query(Matchers.Of<Sid<ObjectId>>());

        var handler = new SliceSumHandler();
        query.ForSlice<Sid<ObjectId>, SliceSumHandler>(ref handler);
        Assert.Equal(496, handler.Sum);
    }

    private struct Position(int value) { public int Value = value; }
    private struct Velocity(int value) { public int Value = value; }
    private readonly record struct ManagedPayload(string Value);

    private static void Move(ref Position position, ref Velocity velocity, List<int> visited)
    {
        visited.Add(position.Value);
        position.Value += velocity.Value;
        velocity.Value = -velocity.Value;
    }

    private struct MovementHandler(List<int> visited) : ISliceHandler<Position, Velocity>
    {
        public int Count;

        public void Handle(ref Position position, ref Velocity velocity)
        {
            Count++;
            Move(ref position, ref velocity, visited);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(63, false)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    [InlineData(65, true)]
    public void QueryForSlice_PreservesOrderAndReferences(int countPerHost, bool useDelegate)
    {
        using var world = new World();
        var entities = new List<Entity>();
        for (var i = 0; i < countPerHost; i++) {
            entities.Add(world.Create(HList.From(new Position(i), new Velocity(i + 1))));
            entities.Add(world.Create(HList.From(new Position(i + countPerHost), new Velocity(i + 1), new ManagedPayload("kept"))));
        }

        var query = world.Query(Matchers.Of<Position, Velocity>());
        var expectedOrder = query.Hosts.SelectMany(host => host).Select(entity => entity.Get<Position>().Value).ToArray();
        var visited = new List<int>();
        if (useDelegate) {
            query.ForSlice((ref Position position, ref Velocity velocity) => Move(ref position, ref velocity, visited));
        }
        else {
            var handler = new MovementHandler(visited);
            query.ForSlice<Position, Velocity, MovementHandler>(ref handler);
            Assert.Equal(entities.Count, handler.Count);
        }

        Assert.Equal(expectedOrder, visited);
        for (var i = 0; i < countPerHost; i++) {
            Assert.Equal(2 * i + 1, entities[2 * i].Get<Position>().Value);
            Assert.Equal(2 * i + countPerHost + 1, entities[2 * i + 1].Get<Position>().Value);
            Assert.Equal(-(i + 1), entities[2 * i].Get<Velocity>().Value);
            Assert.Equal(-(i + 1), entities[2 * i + 1].Get<Velocity>().Value);
            Assert.Equal("kept", entities[2 * i + 1].Get<ManagedPayload>().Value);
        }
    }

    [Fact]
    public void QueryForSlice_PreservesAliasingForRepeatedComponents()
    {
        using var world = new World();
        var entities = Enumerable.Range(0, 65)
            .Select(i => world.Create(HList.From(new Position(i), new ManagedPayload("kept"))))
            .ToArray();

        world.Query(Matchers.Of<Position>()).ForSlice((ref Position first, ref Position second) => {
            first.Value++;
            second.Value += 2;
        });

        Assert.Equal(Enumerable.Range(0, 65).Select(i => i + 3), entities.Select(entity => entity.Get<Position>().Value));
        Assert.All(entities, entity => Assert.Equal("kept", entity.Get<ManagedPayload>().Value));
    }

    [Fact]
    public void QueryForSlice_StopsAtThrowingHandler()
    {
        using var world = new World();
        var entities = Enumerable.Range(0, 65)
            .Select(i => world.Create(HList.From(new Position(i), new Velocity(1), new ManagedPayload("kept"))))
            .ToArray();
        var visited = 0;
        var query = world.Query(Matchers.Of<Position, Velocity>());

        Assert.Throws<InvalidOperationException>(() => query.ForSlice((ref Position position, ref Velocity velocity) => {
            if (++visited == 17) throw new InvalidOperationException();
            position.Value += velocity.Value;
        }));

        Assert.Equal(17, visited);
        Assert.Equal(Enumerable.Range(0, 65).Select(i => i + (i < 16 ? 1 : 0)), entities.Select(entity => entity.Get<Position>().Value));
    }

    [Fact]
    public void EntityHostRecord_Test()
    {
        // Arrange
        var host = new ArrayEntityHost<HList<Sid<ObjectId>, EmptyHList>>();
        host.Create(HList.From(new Sid<ObjectId>(0)));

        // Act & Assert
        var buffer = new Entity[1];
        host.Record(buffer);
        Assert.Single(buffer);
    }
}
