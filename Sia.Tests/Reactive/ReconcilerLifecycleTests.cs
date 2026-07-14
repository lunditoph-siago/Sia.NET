namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

using ValueList = HList<ReactiveValue, EmptyHList>;

public class ReconcilerLifecycleTests
{
    [Fact]
    public void MountStateUpdateAndUnmount_PreserveOutputOwnership()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var probe = new LifecycleProbe();

        var mount = reconciler.Mount(new LifecycleSpec(probe));
        var output = FindOutput(world);

        Assert.Equal(1, world.Count);

        probe.State.Set(2);
        probe.State.Set(3);
        reconciler.Flush();

        Assert.Same(output, FindOutput(world));
        Assert.Equal(3, output.Get<ReactiveValue>().Value);
        Assert.Equal(2, probe.Expansions);

        mount.Unmount();

        Assert.False(mount.IsMounted);
        Assert.False(output.IsValid);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void StaleHandle_CannotMutateAReusedCell()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var stale = reconciler.Mount(new HandleSpec(1));
        stale.Unmount();

        var current = reconciler.Mount(new HandleSpec(2));

        Assert.False(stale.IsMounted);
        Assert.Throws<ObjectDisposedException>(
            () => stale.Update(new HandleSpec(99)));

        current.Update(new HandleSpec(3));
        reconciler.Flush();
        Assert.Equal(3, FindOutput(world).Get<ReactiveValue>().Value);
    }

    private static Entity FindOutput(World world)
    {
        using var query = world.Query(Matchers.Of<ReactiveValue>());
        return Assert.Single(query.Hosts.SelectMany(static host => host));
    }
}

public readonly record struct ReactiveValue(int Value);

public sealed class LifecycleProbe
{
    public StateRef<int> State;
    public int Expansions;
}

public readonly record struct LifecycleSpec(LifecycleProbe Probe)
    : ISpec<LifecycleSpec, int, EntityTerm<ValueList, UnitTerm>>
{
    public static int InitialState(in LifecycleSpec props) => 1;

    public static EntityTerm<ValueList, UnitTerm> Expand(
        in LifecycleSpec props,
        in int state,
        in ExpandContext context)
    {
        props.Probe.State = context.UseState<int>();
        props.Probe.Expansions++;
        return Term.Entity(HList.From(new ReactiveValue(state)));
    }
}

public readonly record struct HandleSpec(int Value)
    : ISpec<HandleSpec, int, EntityTerm<ValueList, UnitTerm>>
{
    public static EntityTerm<ValueList, UnitTerm> Expand(
        in HandleSpec props,
        in int state,
        in ExpandContext context)
        => Term.Entity(HList.From(new ReactiveValue(props.Value)));
}
