using Sia.Reactive;

using static Sia.Reactive.Reactive;

namespace Sia.Tests.Reactive;

public class ReactiveTeardownTests
{
    [Fact]
    public void ChildFirstOutputFailure_PreservesStateUntilRetry()
    {
        using var world = new World();
        var runtime = world.AcquireAddon<Reconciler>();
        var probe = new RetryProbe();
        var child = new ReactiveNodeSpec<ComponentTerm<RetryProps>>(Component(RenderRetryChild, new RetryProps(false, probe)).Term);
        var root = runtime.Mount(new RetryRoot<ReactiveNodeSpec<ComponentTerm<RetryProps>>>(false, child));

        root.Update(new(true, new(Component(RenderRetryChild, new RetryProps(true, probe)).Term)));
        Assert.Throws<InvalidOperationException>(runtime.Flush);
        var retained = probe.State;
        retained.Set(8);
        root.Update(new(true, child));
        runtime.Flush();

        Assert.Equal(8, probe.State.Value);
        Assert.Equal(8, retained.Value);
        Assert.Equal(8, Assert.Single(world.Query<TypeUnion<int>>().Hosts.SelectMany(host => host)).Get<int>());
        Assert.Equal(1, probe.Setups);
        Assert.Equal(0, probe.Cleanups);

        root.Unmount();
        Assert.Equal(0, world.Count);
        Assert.Equal(1, probe.Cleanups);
        Assert.Throws<ObjectDisposedException>(() => retained.Set(9));
    }

    [Fact]
    public void ForEach_ReentrantUnmountPreservesBothChildSnapshots()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var calls = new List<int>();
        var inner = reconciler.Mount(List(100, 2, calls.Add));
        var outer = reconciler.Mount(List(0, 3, key => {
            calls.Add(key);
            if (key == 0) {
                inner.Unmount();
            }
        }));

        outer.Unmount();

        Assert.Equal([0, 100, 101, 1, 2], calls);
        Assert.False(inner.IsMounted);
        Assert.Equal(0, world.Count);

        calls.Clear();
        reconciler.Mount(List(200, 3, calls.Add)).Unmount();
        Assert.Equal([200, 201, 202], calls);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void ForEach_FailedCleanupsStillVisitAllChildrenAndAllowAnotherMount()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var calls = new List<int>();
        var mount = reconciler.Mount(List(0, 3, key => {
            calls.Add(key);
            if (key != 1) {
                throw new InvalidOperationException($"cleanup {key}");
            }
        }));

        var error = Assert.Throws<AggregateException>(mount.Unmount);

        Assert.Equal(["cleanup 0", "cleanup 2"],
            error.InnerExceptions.Select(exception => exception.Message));
        Assert.Equal([0, 1, 2], calls);
        Assert.False(mount.IsMounted);
        Assert.Equal(0, world.Count);

        calls.Clear();
        reconciler.Mount(List(10, 2, calls.Add)).Unmount();
        Assert.Equal([10, 11], calls);
        Assert.Equal(0, world.Count);
    }

    private static TeardownListSpec List(int start, int count, Action<int> cleanup)
    {
        var items = new Keyed<int, TeardownItemSpec>[count];
        for (var i = 0; i < count; i++) {
            items[i] = Term.Keyed(start + i, new TeardownItemSpec(start + i, cleanup));
        }
        return new(items);
    }

    private sealed class RetryProbe
    {
        public State<int> State;
        public int Setups, Cleanups;
    }

    private readonly record struct RetryProps(bool Fail, RetryProbe Probe);
    private readonly record struct RetryDependencies(RetryProbe Probe);

    private static ReactiveNode RenderRetryChild(in RetryProps props, ref Hooks hooks)
    {
        var state = hooks.UseState(2);
        props.Probe.State = state;
        hooks.UseEffect(new RetryDependencies(props.Probe),
            static (in RetryDependencies dependencies) => { dependencies.Probe.Setups++; return dependencies.Probe; },
            static (in RetryProbe probe) => probe.Cleanups++);
        return new ReactiveNode<Group<EntityTerm<HList<int, EmptyHList>, UnitTerm>, RetryFailure>>(
            Term.Group(Term.Entity(HList.From(state.Value)), new RetryFailure(props.Fail)));
    }

    private readonly record struct RetryRoot<TSpec>(bool Visible, TSpec Child)
        : ISpec<RetryRoot<TSpec>, Unit, CondTerm<LiftTerm<TSpec>>>
        where TSpec : struct, ISpec, IEquatable<TSpec>
    {
        public static CondTerm<LiftTerm<TSpec>> Expand(in RetryRoot<TSpec> props, in Unit state, in ExpandContext context)
            => Term.Cond(props.Visible, Term.Lift(props.Child));
    }

    private readonly record struct RetryFailure(bool Fail) : ITerm<RetryFailure>
    {
        public static int SlotCount => 0;
        public static void Mount(in RetryFailure self, ref GraphContext context)
        {
            if (self.Fail) throw new InvalidOperationException("output failed");
        }
        public static void Reconcile(in RetryFailure previous, in RetryFailure next, ref GraphContext context) => Mount(next, ref context);
    }
}

internal readonly record struct TeardownListSpec(
    ReadOnlyMemory<Keyed<int, TeardownItemSpec>> Items)
    : ISpec<TeardownListSpec, Unit, ForEachTerm<int, TeardownItemSpec>>
{
    public static ForEachTerm<int, TeardownItemSpec> Expand(
        in TeardownListSpec props, in Unit state, in ExpandContext context)
        => Term.ForEach<int, TeardownItemSpec>(props.Items);
}

internal readonly record struct TeardownItemSpec(int Key, Action<int> Cleanup)
    : ISpec<TeardownItemSpec, Unit, EffectTerm<TeardownEffect>>
{
    public static EffectTerm<TeardownEffect> Expand(
        in TeardownItemSpec props, in Unit state, in ExpandContext context)
        => Term.Effect(new TeardownEffect(props.Key, props.Cleanup));
}

internal readonly record struct TeardownEffect(int Key, Action<int> Cleanup)
    : IEffect<TeardownEffect>
{
    public static void Mount(in TeardownEffect self) {}
    public static void Reconcile(in TeardownEffect previous, in TeardownEffect next) {}
    public static void Unmount(in TeardownEffect self) => self.Cleanup(self.Key);
}
