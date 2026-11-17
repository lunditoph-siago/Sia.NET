using EmptyHost = Sia.WorldEntityHost<Sia.EmptyHList, Sia.ArrayEntityHost<Sia.EmptyHList>>;

namespace Sia.Tests.Worlds;

public class WorldHostLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MigrationCallbacksKeepTheEntityAndItsDataTogether(bool createSibling)
    {
        using var world = new World();
        var other = world.Create(HList.From(11));
        var entity = world.Create(HList.From(22));
        var source = (IReactiveEntityHost)entity.Host;
        if (createSibling) {
            world.OnEntityHostAdded += _ => Reenter();
        }
        else {
            world.AcquireHost<HList<bool, HList<int, EmptyHList>>, ArrayEntityHost<HList<bool, HList<int, EmptyHList>>>>();
            source.OnEntityMovedOut += (_, _) => Reenter();
        }

        entity.Add(true);

        Assert.False(other.IsValid);
        Assert.True(entity.Get<bool>());
        Assert.Equal(22, entity.Get<int>());
        Assert.Equal(0, source.Count);
        Assert.Equal(1, world.Count);

        void Reenter()
        {
            Assert.Throws<InvalidOperationException>(() => entity.Destroy());
            other.Destroy();
        }
    }

    [Fact]
    public void HostInitializationPublishesOnlyAfterSuccess()
    {
        using var world = new World();
        var error = new InvalidOperationException();
        world.AddAddon(new HostInitializer(owner => {
            Assert.Empty(owner.Hosts);
            Assert.Throws<InvalidOperationException>(() => owner.AcquireHost<EmptyHList, InitializingHost>());
            throw error;
        }));

        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => world.AcquireHost<EmptyHList, InitializingHost>()));
        Assert.Empty(world.Hosts);
        Assert.Equal(0, world.Version);

        world.RemoveAddon<HostInitializer>();
        Assert.Same(world.AcquireHost<EmptyHList, InitializingHost>(), Assert.Single(world.Hosts));
    }

    [Fact]
    public void AddonInitializationCanRetryAfterReentryFails()
    {
        using var world = new World();
        var nested = new Initializer(_ => {});
        var addon = new Initializer(owner => owner.AddAddon(nested));

        Assert.Throws<InvalidOperationException>(() => world.AddAddon(addon));
        Assert.Empty(world.Addons);
        Assert.Same(nested, world.AddAddon(nested));
        Assert.Same(nested, Assert.Single(world.Addons));
    }

    [Theory]
    [InlineData("release")]
    [InlineData("clear")]
    [InlineData("clear empty")]
    public void HostTeardownStillFinishesAfterObserversThrow(string operation)
    {
        using var world = new World();
        var host = world.AcquireHost<EmptyHList, ArrayEntityHost<EmptyHList>>();
        var entity = operation == "clear empty" ? default : host.Create();
        var error = new InvalidOperationException();
        var notified = 0;
        world.OnEntityHostRemoved += Fail;
        world.OnEntityHostRemoved += _ => notified++;
        var thrown = Record.Exception(() => {
            switch (operation) {
                case "release": world.ReleaseHost<EmptyHost>(); break;
                case "clear": world.ClearHosts(); break;
                case "clear empty": world.ClearEmptyHosts(); break;
            }
        });
        world.OnEntityHostRemoved -= Fail;

        Assert.Same(error, thrown);
        Assert.Equal(1, notified);
        Assert.Empty(world.Hosts);
        Assert.False(entity.IsValid);
        Assert.Equal(0, world.Count);
        Assert.Throws<ObjectDisposedException>(() => host.Create());

        void Fail(IReactiveEntityHost _) => throw error;
    }

    private sealed class InitializingHost : ArrayEntityHost<EmptyHList>, IEntityHost
    {
        void IEntityHost.OnInitialize(World world)
        {
            if (world.TryGetAddon<HostInitializer>(out var initializer)) initializer.Initialize(world);
        }
    }

    private sealed record HostInitializer(Action<World> Initialize) : IAddon;

    private sealed record Initializer(Action<World> Initialize) : IAddon
    {
        public void OnInitialize(World world) => Initialize(world);
    }
}
