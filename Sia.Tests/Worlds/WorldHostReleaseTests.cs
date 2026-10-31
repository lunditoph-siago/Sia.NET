namespace Sia.Tests.Worlds;

public sealed class WorldHostReleaseTests
{
    private readonly record struct Ping : IEvent;

    [Fact]
    public void ReleaseHost_DuringDispatch_RejectsBeforeAnyEffects()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<int, EmptyHList>>();
        var entity = host.Create(HList.From(42));
        var version = world.Version;
        var removals = 0;
        var releases = 0;
        var disposals = 0;
        var laterListeners = 0;
        world.Dispatcher.Listen<WorldEvents.Remove>((Entity _, in WorldEvents.Remove e) => { removals++; return false; });
        host.OnEntityReleased += _ => releases++;
        host.OnDisposed += _ => disposals++;
        world.Dispatcher.Listen<Ping>((Entity _, in Ping e) => {
            Assert.Throws<InvalidOperationException>(() =>
                world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>());
            Assert.Equal(version, world.Version);
            Assert.Equal(0, removals);
            Assert.Equal(0, releases);
            Assert.Equal(0, disposals);
            Assert.Equal(1, world.Count);
            Assert.Same(host, Assert.Single(world.Hosts));
            return false;
        });
        world.Dispatcher.Listen<Ping>((Entity e, in Ping _) => {
            Assert.Equal(42, e.Get<int>());
            laterListeners++;
            return false;
        });

        world.Send(entity, new Ping());

        Assert.Equal(1, laterListeners);
        Assert.True(world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>());
        Assert.Equal(1, removals);
        Assert.Equal(1, releases);
        Assert.Equal(1, disposals);
        Assert.Equal(0, world.Count);
        Assert.Empty(world.Hosts);
        Assert.False(entity.IsValid);
    }

    [Fact]
    public void ReleaseHost_AfterSend_CompletesSynchronously()
    {
        using var world = new World();
        var entity = world.Create(HList.From(42));
        var released = false;
        world.Dispatcher.Listen<Ping>((Entity e, in Ping _) => {
            world.Dispatcher.RunAfterSend(() => {
                released = world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>();
                Assert.False(e.IsValid);
                Assert.Equal(0, world.Count);
            });
            Assert.True(e.IsValid);
            return false;
        });

        world.Send(entity, new Ping());

        Assert.True(released);
        Assert.Empty(world.Hosts);
    }

    [Fact]
    public void ReleaseHost_WithPendingEntity_RejectsBeforeReleasingOtherEntities()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<int, EmptyHList>>();
        var first = host.Create(HList.From(1));
        var pending = host.Create(HList.From(2));
        var releases = 0;
        host.OnEntityReleased += _ => releases++;
        world.Dispatcher.Listen<Ping>((Entity _, in Ping e) => {
            world.Dispatcher.RunAfterSend(() => {
                Assert.False(world.Dispatcher.IsSending);
                Assert.Throws<InvalidOperationException>(() =>
                    world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>());
                Assert.Equal(0, releases);
                Assert.Equal(2, world.Count);
                Assert.Equal(1, first.Get<int>());
                Assert.Equal(2, pending.Get<int>());
            });
            pending.Destroy();
            return false;
        });

        world.Send(first, new Ping());

        Assert.Equal(1, releases);
        Assert.True(first.IsValid);
        Assert.False(pending.IsValid);
        Assert.Equal(1, world.Count);
        Assert.True(world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>());
        Assert.Equal(2, releases);
        Assert.Equal(0, world.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseHost_FromEntityReleaseCallback_RejectsReentrancy(bool owned)
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<int, EmptyHList>>();
        var entity = host.Create();
        var calls = 0;
        host.OnEntityReleased += e => {
            calls++;
            Assert.False(world.Dispatcher.IsSending);
            Assert.Throws<InvalidOperationException>(() =>
                world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>());
            Assert.True(e.IsValid);
            Assert.Same(host, Assert.Single(world.Hosts));
        };

        if (owned) host.ReleaseOwned(entity);
        else entity.Destroy();

        Assert.Equal(1, calls);
        Assert.Equal(0, world.Count);
        Assert.False(entity.IsValid);
        Assert.True(world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>());
        Assert.Empty(world.Hosts);
    }

    [Fact]
    public void ReleaseHost_WhenCallbackQueuesAnotherRelease_PreservesHostUntilCleanup()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<int, EmptyHList>>();
        var first = host.Create(HList.From(1));
        var second = host.Create(HList.From(2));
        var released = new List<Entity>();
        var disposed = false;
        host.OnEntityReleased += released.Add;
        host.OnDisposed += _ => disposed = true;
        world.Dispatcher.Listen<WorldEvents.Remove<int>>((Entity e, in WorldEvents.Remove<int> _) => {
            if (e == first) second.Destroy();
            return false;
        });
        world.Dispatcher.Listen<Ping>((Entity e, in Ping _) => {
            world.Dispatcher.RunAfterSend(() => {
                Assert.Throws<InvalidOperationException>(() =>
                    world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>());
                Assert.True(second.IsValid);
                Assert.Equal(1, world.Count);
                Assert.False(disposed);
                Assert.Same(host, Assert.Single(world.Hosts));
            });
            return false;
        });

        world.Send(first, new Ping());

        Assert.Equal(new[] { first, second }, released);
        Assert.False(first.IsValid);
        Assert.False(second.IsValid);
        Assert.Equal(0, world.Count);
        Assert.True(world.ReleaseHost<WorldEntityHost<HList<int, EmptyHList>, ArrayEntityHost<HList<int, EmptyHList>>>>());
        Assert.True(disposed);
        Assert.Empty(world.Hosts);
    }
}
