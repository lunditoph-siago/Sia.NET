namespace Sia.Tests.Worlds;

public class WorldAddonTests
{
    private abstract class ProbeAddon : IAddon
    {
        public int UninitializeCount { get; private set; }

        public void OnUninitialize(World world)
            => UninitializeCount++;
    }

    private sealed class FirstAddon : ProbeAddon;
    private sealed class SecondAddon : ProbeAddon;
    private sealed class ThirdAddon : ProbeAddon;

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
}
