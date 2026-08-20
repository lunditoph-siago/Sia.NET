namespace Sia.Tests.Reactors;

using Sia.Reactors;

public class MapperTests
{
    private readonly record struct ObjectId(Guid Value);

    [Fact]
    public void CreatingAnEntityWithASid_RegistersItInTheMapper()
    {
        using var world = new World();
        var mapper = world.AcquireAddon<Mapper<ObjectId>>();
        var id = new ObjectId(Guid.NewGuid());

        var entity = world.Create(HList.From(Sid.From(id)));

        Assert.Equal(entity, mapper[id]);
    }

    [Fact]
    public void SettingSid_RemapsTheEntityToTheNewId()
    {
        using var world = new World();
        var mapper = world.AcquireAddon<Mapper<ObjectId>>();
        var previousId = new ObjectId(Guid.NewGuid());
        var entity = world.Create(HList.From(Sid.From(previousId)));

        var newId = new ObjectId(Guid.NewGuid());
        entity.SetSid(newId);

        Assert.Equal(entity, mapper[newId]);
        Assert.False(mapper.ContainsKey(previousId));
    }
}
