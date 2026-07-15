namespace Sia.Tests.Worlds;

public class WorldAddonTests
{
    private abstract class ProbeAddon : IAddon
    {
        public int UninitializeCount { get; private set; }
        public List<string>? Calls { get; set; }
        public Exception? Error { get; set; }

        protected abstract string Name { get; }

        public void OnUninitialize(World world)
        {
            UninitializeCount++;
            Calls?.Add(Name);
            if (Error != null) {
                throw Error;
            }
        }
    }

    private sealed class FirstAddon : ProbeAddon
    {
        protected override string Name => "first";
    }

    private sealed class SecondAddon : ProbeAddon
    {
        protected override string Name => "second";
    }

    private sealed class ThirdAddon : ProbeAddon
    {
        protected override string Name => "third";
    }

    [Fact]
    public void SparseRegistry_EnumeratesUniquelyAndClearsEveryRemainingAddon()
    {
        using var world = new World();
        var first = world.AddAddon<FirstAddon>();
        var removed = world.AddAddon<SecondAddon>();
        var third = world.AddAddon<ThirdAddon>();

        Assert.True(world.RemoveAddon<SecondAddon>());

        Assert.Equal([first, third], world.Addons);
        Assert.Same(first, Assert.Single(world.GetAddons<FirstAddon>()));
        Assert.Equal([first, third], world.GetAddons<ProbeAddon>());

        world.ClearAddons();

        Assert.Empty(world.Addons);
        Assert.Equal(1, first.UninitializeCount);
        Assert.Equal(1, removed.UninitializeCount);
        Assert.Equal(1, third.UninitializeCount);
        Assert.False(world.TryGetAddon<FirstAddon>(out _));
        Assert.False(world.TryGetAddon<SecondAddon>(out _));
        Assert.False(world.TryGetAddon<ThirdAddon>(out _));
    }

    [Fact]
    public void ClearAddonsPreservesOrderAndReportsErrorsAfterEveryCleanup()
    {
        var calls = new List<string>();
        var firstError = new InvalidOperationException("first");
        var secondError = new ArgumentException("second");
        using var world = new World();
        var first = world.AddAddon<FirstAddon>();
        var second = world.AddAddon<SecondAddon>();
        var third = world.AddAddon<ThirdAddon>();
        first.Calls = second.Calls = third.Calls = calls;
        first.Error = firstError;
        second.Error = secondError;

        var thrown = Assert.Throws<AggregateException>(world.ClearAddons);

        Assert.Equal(["first", "second", "third"], calls);
        Assert.Equal([firstError, secondError], thrown.InnerExceptions);
        Assert.Empty(world.Addons);
        world.ClearAddons();
        Assert.Equal(1, first.UninitializeCount);
        Assert.Equal(1, second.UninitializeCount);
        Assert.Equal(1, third.UninitializeCount);
    }

    [Fact]
    public void DisposeStillCleansAddonsWhenHostCleanupFails()
    {
        var hostError = new InvalidOperationException("host");
        var world = new World();
        var entity = world.Create();
        entity.Host.OnDisposed += _ => throw hostError;
        var addon = world.AddAddon<FirstAddon>();

        var thrown = Assert.Throws<InvalidOperationException>(world.Dispose);

        Assert.Same(hostError, thrown);
        Assert.Equal(1, addon.UninitializeCount);
        Assert.Empty(world.Addons);
        Assert.True(world.IsDisposed);
    }
}
