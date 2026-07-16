namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

using ValueList = HList<ReactiveValue, EmptyHList>;
using FailingTree = global::Sia.Reactive.Group<
    global::Sia.Reactive.EntityTerm<HList<ReactiveValue, EmptyHList>,
        global::Sia.Reactive.UnitTerm>,
    FailingTerm>;

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

        probe.MutateDuringExpansion = true;
        mount.Invalidate();
        var error = Assert.Throws<InvalidOperationException>(reconciler.Flush);
        Assert.Contains("while its spec is expanding", error.Message);
        Assert.Equal(3, output.Get<ReactiveValue>().Value);

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

    [Fact]
    public void FailedMount_RollsBackCreatedOutputsAndPreservesTheCause()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();

        var error = Assert.Throws<ReactiveMountException>(
            () => reconciler.Mount(new FailingSpec(42)));

        Assert.Equal("mount failed", error.Message);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void FunctionSpecUsesTheSameValueReconciliationPath()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var mount = reconciler.Mount(
            Spec.Of<int, EntityTerm<ValueList, UnitTerm>>(4, ExpandValue));
        var output = FindOutput(world);

        mount.Update(Spec.Of<int, EntityTerm<ValueList, UnitTerm>>(5, ExpandValue));
        reconciler.Flush();

        Assert.Same(output, FindOutput(world));
        Assert.Equal(5, output.Get<ReactiveValue>().Value);
    }

    private static EntityTerm<ValueList, UnitTerm> ExpandValue(
        in int value,
        in ExpandContext context)
        => Term.Entity(HList.From(new ReactiveValue(value)));

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
    public bool MutateDuringExpansion;
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
        if (props.Probe.MutateDuringExpansion) {
            props.Probe.State.Set(4);
        }
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

public readonly record struct FailingSpec(int Value)
    : ISpec<FailingSpec, int, FailingTree>
{
    public static FailingTree Expand(
        in FailingSpec props,
        in int state,
        in ExpandContext context)
        => Term.Group(
            Term.Entity(HList.From(new ReactiveValue(props.Value))),
            new FailingTerm());
}

public readonly record struct FailingTerm : ITerm<FailingTerm>
{
    public static int SlotCount => 0;

    public static void Mount(in FailingTerm self, ref GraphContext context)
        => throw new ReactiveMountException("mount failed");

    public static void Reconcile(
        in FailingTerm previous,
        in FailingTerm next,
        ref GraphContext context)
        => throw new ReactiveMountException("reconcile failed");
}

public sealed class ReactiveMountException(string message) : Exception(message);
