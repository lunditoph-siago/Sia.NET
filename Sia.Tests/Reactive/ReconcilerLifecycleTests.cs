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

        var cell = reconciler.Mount(new LifecycleSpec(probe));
        var output = FindOutput(world);

        probe.State.Set(2);
        probe.State.Set(3);
        reconciler.Flush();

        Assert.Same(output, FindOutput(world));
        Assert.Equal(3, output.Get<ReactiveValue>().Value);
        Assert.Equal(2, probe.Expansions);

        reconciler.Unmount(cell);

        Assert.False(cell.IsValid);
        Assert.False(output.IsValid);
        Assert.Equal(0, world.Count);
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
