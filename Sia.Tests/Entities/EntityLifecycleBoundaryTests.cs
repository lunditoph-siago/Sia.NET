namespace Sia.Tests.Entities;

public sealed class EntityLifecycleBoundaryTests
{
    private readonly record struct Ping : IEvent;
    private readonly record struct Pong : IEvent;

    private sealed class RecyclingHost : ArrayEntityHost<HList<int, EmptyHList>>
    {
        public Action? AfterRelease;

        public override void Release(Entity entity)
        {
            base.Release(entity);
            var action = AfterRelease;
            AfterRelease = null;
            action?.Invoke();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RecycledIdentity_KeepsPendingReleaseWhenOldReleaseReturns(bool owned, bool failOldRelease)
    {
        using var world = new World();
        var host = world.AcquireHost<HList<int, EmptyHList>, RecyclingHost>();
        var original = host.Create(HList.From(1));
        Entity replacement = default;
        var error = new InvalidOperationException("custom release");
        var releases = new List<Entity>();
        var checkedReservation = false;
        host.OnEntityReleased += releases.Add;

        void Release(Entity entity)
        {
            if (owned) host.ReleaseOwned(entity);
            else host.Release(entity);
        }

        world.Dispatcher.Listen<Ping>((Entity e, in Ping _) => { Release(e); return false; });
        host.InnerHost.AfterRelease = () => {
            replacement = host.Create(HList.From(2));
            world.Send(replacement, new Ping());
            Assert.True(replacement.IsValid);
            if (failOldRelease) throw error;
        };
        world.Dispatcher.Listen<Pong>((Entity e, in Pong _) => {
            Release(e);
            world.Dispatcher.RunAfterSend(() => {
                Assert.True(replacement.IsValid);
                Assert.Throws<InvalidOperationException>(() => replacement.Add(3L));
                Assert.Equal(2, replacement.Get<int>());
                Release(replacement);
                checkedReservation = true;
            });
            return false;
        });

        var failure = Record.Exception(() => world.Send(original, new Pong()));

        if (failOldRelease) Assert.Same(error, failure);
        else Assert.Null(failure);
        Assert.True(checkedReservation);
        Assert.Equal(new[] { original, replacement }, releases);
        Assert.False(original.IsValid);
        Assert.False(replacement.IsValid);
        Assert.Equal(0, host.Count);
        Assert.Equal(0, world.Count);
        Assert.Equal(0, world.Dispatcher.PendingAfterSendActionCount);
    }

    [Theory]
    [InlineData("host", false)]
    [InlineData("entity", false)]
    [InlineData("add", false)]
    [InlineData("set", false)]
    [InlineData("host", true)]
    [InlineData("entity", true)]
    [InlineData("add", true)]
    [InlineData("set", true)]
    public void Create_StopsNotificationsAfterIdentityIsReleased(string phase, bool owned)
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<int, HList<long, EmptyHList>>>();
        var trace = new List<string>();

        void Observe(Entity entity, string current)
        {
            Assert.True(entity.IsValid);
            trace.Add(current);
            if (current != phase) return;
            if (owned) host.ReleaseOwned(entity);
            else entity.Destroy();
        }

        host.OnEntityCreated += e => Observe(e, "host");
        world.Dispatcher.Listen<WorldEvents.Add>((Entity e, in WorldEvents.Add _) => { Observe(e, "entity"); return false; });
        world.Dispatcher.Listen<WorldEvents.Add<int>>((Entity e, in WorldEvents.Add<int> _) => { Observe(e, "add"); return false; });
        world.Dispatcher.Listen<WorldEvents.Set<int>>((Entity e, in WorldEvents.Set<int> _) => { Observe(e, "set"); return false; });
        world.Dispatcher.Listen<WorldEvents.Add<long>>((Entity e, in WorldEvents.Add<long> _) => { Observe(e, "tail-add"); return false; });
        world.Dispatcher.Listen<WorldEvents.Set<long>>((Entity e, in WorldEvents.Set<long> _) => { Observe(e, "tail-set"); return false; });

        var entity = host.Create(HList.From(1, 2L), out var acquired);

        string[] order = ["host", "entity", "add", "set"];
        Assert.Equal(order.Take(Array.IndexOf(order, phase) + 1), trace);
        Assert.Equal(entity, acquired);
        Assert.False(entity.IsValid);
        Assert.Equal(0, world.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Add_StopsNotificationsAfterIdentityIsReleased(bool many, bool owned)
    {
        using var world = new World();
        var entity = world.Create();
        var trace = new List<string>();
        world.Dispatcher.Listen<WorldEvents.Add<int>>((Entity e, in WorldEvents.Add<int> _) => {
            Assert.Equal(42, e.Get<int>());
            trace.Add("add");
            if (!owned) e.Destroy();
            else if (many) world.GetArrayHost<HList<int, HList<long, EmptyHList>>>().ReleaseOwned(e);
            else world.GetArrayHost<HList<int, EmptyHList>>().ReleaseOwned(e);
            return false;
        });
        world.Dispatcher.Listen<WorldEvents.Set<int>>((Entity e, in WorldEvents.Set<int> _) => {
            Assert.Equal(42, e.Get<int>());
            trace.Add("set");
            return false;
        });
        world.Dispatcher.Listen<WorldEvents.Add<long>>((Entity e, in WorldEvents.Add<long> _) => { trace.Add("tail-add"); return false; });
        world.Dispatcher.Listen<WorldEvents.Set<long>>((Entity e, in WorldEvents.Set<long> _) => { trace.Add("tail-set"); return false; });

        if (many) entity.AddMany(HList.From(42, 43L));
        else entity.Add(42);

        Assert.Equal(new[] { "add" }, trace);
        Assert.False(entity.IsValid);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void Add_PendingReleaseInOuterDispatch_PreservesRemainingNotifications()
    {
        using var world = new World();
        var entity = world.Create();
        var trace = new List<string>();
        world.Dispatcher.Listen<WorldEvents.Add<int>>((Entity e, in WorldEvents.Add<int> _) => {
            e.Destroy();
            trace.Add("add");
            return false;
        });
        world.Dispatcher.Listen<WorldEvents.Set<int>>((Entity e, in WorldEvents.Set<int> _) => {
            Assert.Equal(42, e.Get<int>());
            trace.Add("set");
            return false;
        });
        world.Dispatcher.Listen<Ping>((Entity e, in Ping _) => {
            e.Add(42);
            Assert.True(e.IsValid);
            return false;
        });

        world.Send(entity, new Ping());

        Assert.Equal(new[] { "add", "set" }, trace);
        Assert.False(entity.IsValid);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void Create_RecycledState_DoesNotResumeNotificationsForOldIdentity()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<int, EmptyHList>>();
        var replaced = false;
        Entity replacement = default;
        var additions = new List<Entity>();
        host.OnEntityCreated += e => {
            if (replaced) return;
            replaced = true;
            e.Destroy();
            replacement = host.Create(HList.From(2));
        };
        world.Dispatcher.Listen<WorldEvents.Add>((Entity e, in WorldEvents.Add _) => { additions.Add(e); return false; });

        var original = host.Create(HList.From(1), out var acquired);

        Assert.Equal(original, acquired);
        Assert.False(original.IsValid);
        Assert.True(replacement.IsValid);
        Assert.Equal(2, replacement.Get<int>());
        Assert.Equal(new[] { replacement }, additions);
        Assert.Equal(1, world.Count);
    }
}
