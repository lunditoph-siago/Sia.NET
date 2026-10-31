namespace Sia.Tests.Entities;

public class EntityAddRemoveTests
{
    private record struct Position(float X, float Y);
    private record struct Velocity(float X, float Y);
    private record struct Tag;

    private sealed class TrackingHost<T> : ArrayEntityHost<T> where T : struct, IHList
    {
        public int AddCalls;
        public int RemoveCalls;

        public override void Add<TComponent>(Entity entity, in TComponent value)
        {
            AddCalls++;
            base.Add(entity, value);
        }

        public override void Remove<TComponent>(Entity entity, out bool success)
        {
            RemoveCalls++;
            base.Remove<TComponent>(entity, out success);
        }
    }

    [Fact]
    public void AddRemove_PreserveDerivedHostOverrides()
    {
        using var world = new World();
        var source = new TrackingHost<HList<Position, EmptyHList>>();
        var target = new TrackingHost<HList<Velocity, HList<Position, EmptyHList>>>();
        var host = world.UnsafeAddRawHost(
            new WorldEntityHost<HList<Position, EmptyHList>, ArrayEntityHost<HList<Position, EmptyHList>>>(world, source));
        world.UnsafeAddRawHost(
            new WorldEntityHost<HList<Velocity, HList<Position, EmptyHList>>, ArrayEntityHost<HList<Velocity, HList<Position, EmptyHList>>>>(world, target));
        var entity = host.Create(HList.From(new Position(1, 2)));

        entity.Add(new Velocity(3, 4));
        Assert.Equal(1, source.AddCalls);
        Assert.Equal(new Velocity(3, 4), entity.Get<Velocity>());

        entity.Remove<Velocity>();
        Assert.Equal(1, target.RemoveCalls);
        Assert.False(entity.Contains<Velocity>());
        Assert.Equal(new Position(1, 2), entity.Get<Position>());
    }

    [Fact]
    public void Add_DispatchesSetAfterDeferredListenerRegistration()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new Position(1, 2)));
        var events = new List<string>();
        world.Dispatcher.Listen<WorldEvents.Add<Velocity>>((Entity _, in WorldEvents.Add<Velocity> _) => {
            events.Add("add");
            world.Dispatcher.RunAfterSend(() => world.Dispatcher.Listen<WorldEvents.Set<Velocity>>(
                (Entity _, in WorldEvents.Set<Velocity> _) => { events.Add("set"); return false; }));
            return false;
        });

        entity.Add(new Velocity(3, 4));

        Assert.Equal(["add", "set"], events);
    }

    [Fact]
    public void Remove_NonHeadComponent_Test()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new Tag(), new Velocity(3, 4), new Position(1, 2)));

        entity.Remove<Velocity>();

        Assert.False(entity.Contains<Velocity>());
        Assert.True(entity.Contains<Tag>());
        Assert.Equal(new Position(1, 2), entity.Get<Position>());
    }

    [Fact]
    public void Remove_HeadComponent_Test()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new Tag(), new Position(1, 2)));

        entity.Remove<Tag>();

        Assert.False(entity.Contains<Tag>());
        Assert.Equal(new Position(1, 2), entity.Get<Position>());
    }

    [Fact]
    public void Remove_SwapRemovedSlot_Test()
    {
        using var world = new World();
        var e0 = world.Create(HList.From(new Tag(), new Position(1, 2)));
        var e1 = world.Create(HList.From(new Tag(), new Position(3, 4)));
        var e2 = world.Create(HList.From(new Tag(), new Position(5, 6)));

        e0.Remove<Tag>();

        Assert.False(e0.Contains<Tag>());
        Assert.Equal(new Position(1, 2), e0.Get<Position>());
        Assert.True(e1.Contains<Tag>());
        Assert.Equal(new Position(3, 4), e1.Get<Position>());
        Assert.True(e2.Contains<Tag>());
        Assert.Equal(new Position(5, 6), e2.Get<Position>());
    }

    [Fact]
    public void Remove_NonZeroLastSlot_Test()
    {
        using var world = new World();
        var e0 = world.Create(HList.From(new Tag(), new Position(1, 2)));
        var e1 = world.Create(HList.From(new Tag(), new Position(3, 4)));

        e1.Remove<Tag>();

        Assert.True(e0.Contains<Tag>());
        Assert.Equal(new Position(1, 2), e0.Get<Position>());
        Assert.False(e1.Contains<Tag>());
        Assert.Equal(new Position(3, 4), e1.Get<Position>());
    }

    [Fact]
    public void Add_SlotNumberMismatch_Test()
    {
        using var world = new World();
        var e0 = world.Create(HList.From(new Position(1, 2)));
        var e1 = world.Create(HList.From(new Position(3, 4)));

        e1.Add(new Velocity(9, 9));

        Assert.Equal(new Position(1, 2), e0.Get<Position>());
        Assert.False(e0.Contains<Velocity>());
        Assert.Equal(new Position(3, 4), e1.Get<Position>());
        Assert.Equal(new Velocity(9, 9), e1.Get<Velocity>());
    }

    [Fact]
    public void Add_NonEmptyTargetHost_Test()
    {
        using var world = new World();
        var occupant = world.Create(HList.From(new Velocity(9, 9), new Position(3, 4)));
        var entity = world.Create(HList.From(new Position(1, 2)));

        entity.Add(new Velocity(7, 7));

        Assert.Equal(new Position(1, 2), entity.Get<Position>());
        Assert.Equal(new Velocity(7, 7), entity.Get<Velocity>());
        Assert.Equal(new Position(3, 4), occupant.Get<Position>());
        Assert.Equal(new Velocity(9, 9), occupant.Get<Velocity>());
    }

    [Fact]
    public void HandleIdentity_RemainsStableAcrossArchetypeMigration()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new Position(1, 2)));
        var copy = entity;
        var entities = new HashSet<Entity> { entity };
        var hashCode = entity.GetHashCode();

        entity.Add(new Velocity(3, 4));
        entity.Remove<Velocity>();

        Assert.Equal(copy, entity);
        Assert.Equal(hashCode, entity.GetHashCode());
        Assert.Contains(copy, entities);
        Assert.Equal(new Position(1, 2), entity.Get<Position>());
    }

    private sealed class Creator(World world) : IGenericStructHandler<IHList>
    {
        public Entity? Result;
        public void Handle<T>(in T value) where T : struct, IHList
            => Result = world.Create(value);
    }

    [Fact]
    public void Remove_DynBundleCreatedEntity_Test()
    {
        using var world = new World();
        var bundle = new DynBundle()
            .Add(new Position(1, 2))
            .Add(new Tag());

        var creator = new Creator(world);
        bundle.ToHList(creator);
        var entity = creator.Result
            ?? throw new InvalidOperationException("Dynamic bundle did not create an entity.");
        Assert.Equal(new Position(1, 2), entity.Get<Position>());

        entity.Remove<Tag>();

        Assert.False(entity.Contains<Tag>());
        Assert.Equal(new Position(1, 2), entity.Get<Position>());
    }
}
