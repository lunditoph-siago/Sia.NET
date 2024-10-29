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
        host.Handle((refHost, from, to) =>
        {
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
        host.ForEach((EntityHandler)(entity =>
        {
            ref var id = ref entity.Get<Sid<ObjectId>>();
            Assert.Equal(0, id.Value.Value);
        }));
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