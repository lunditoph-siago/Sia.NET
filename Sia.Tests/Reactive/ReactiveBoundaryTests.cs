namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

using MarkerList = HList<BoundaryMarker, EmptyHList>;

public class ReactiveBoundaryTests(QueryTestHelpers helpers) : IClassFixture<QueryTestHelpers>
{
    private readonly record struct SharedSchedule;

    private sealed class BoundaryLog
    {
        public readonly List<string> Calls = [];
    }

    [Fact]
    public void TwoIndependentRoots_SharingAScheduleLabel_BothTick()
    {
        using var world = new World();
        var logA = new BoundaryLog();
        var logB = new BoundaryLog();
        var scheduler = world.AcquireAddon<Scheduler>();

        world.AcquireAddon<Reconciler>();
        var mountA = world.Mount(
            (in Unit _, ref Hooks _) => Reactive.Schedule(
                default(SharedSchedule),
                Reactive.System<RootASystem>()),
            default(Unit));
        var mountB = world.Mount(
            (in Unit _, ref Hooks _) => Reactive.Schedule(
                default(SharedSchedule),
                Reactive.System<RootBSystem>()),
            default(Unit));

        RootASystem.Log = logA;
        RootBSystem.Log = logB;

        scheduler.TickSchedule(ScheduleLabel.For<SharedSchedule>());

        Assert.Equal(["a"], logA.Calls);
        Assert.Equal(["b"], logB.Calls);

        mountA.Unmount();
        scheduler.TickSchedule(ScheduleLabel.For<SharedSchedule>());
        Assert.Equal(["a"], logA.Calls);
        Assert.Equal(["b", "b"], logB.Calls);

        mountB.Unmount();
    }

    private sealed class RootASystem() : SystemBase(Matchers.Any)
    {
        public static BoundaryLog? Log;
        public override void Execute(WorldContext context, IEntityQuery query) => Log?.Calls.Add("a");
    }

    private sealed class RootBSystem() : SystemBase(Matchers.Any)
    {
        public static BoundaryLog? Log;
        public override void Execute(WorldContext context, IEntityQuery query) => Log?.Calls.Add("b");
    }

    [Fact]
    public void Either_SwitchesBranchesAndDestroysTheOutgoingOneInReverseOrder()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var calls = new List<string>();
        var probe = new EitherProbe();
        var mount = reconciler.Mount(new EitherSpec(calls, probe));

        Assert.Equal(["mount first.1", "mount first.2"], calls);
        calls.Clear();

        probe.IsFirst.Set(false);
        reconciler.Flush();

        Assert.Equal(["unmount first.2", "unmount first.1", "mount second"], calls);
        calls.Clear();

        probe.IsFirst.Set(true);
        reconciler.Flush();

        Assert.Equal(["unmount second", "mount first.1", "mount first.2"], calls);

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void Cond_FailingUnmountOfOneSiblingStillDestroysTheOtherAndAggregates()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var calls = new List<string>();
        var probe = new FailingTeardownProbe();
        var mount = reconciler.Mount(new FailingTeardownSpec(calls, probe));

        Assert.Equal(["mount ok", "mount failing"], calls);
        calls.Clear();

        probe.Visible.Set(false);
        var error = Assert.Throws<InvalidOperationException>(reconciler.Flush);
        Assert.Equal("teardown failed", error.Message);

        Assert.Equal(["unmount failing", "unmount ok"], calls);

        mount.Unmount();
    }

    [Fact]
    public void RapidSuccessiveEitherFlips_WithinASingleFlush_CollapseToOneNetTransition()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var calls = new List<string>();
        var probe = new EitherProbe();
        var mount = reconciler.Mount(new EitherSpec(calls, probe));
        calls.Clear();

        probe.IsFirst.Set(false);
        probe.IsFirst.Set(true);
        probe.IsFirst.Set(false);
        probe.IsFirst.Set(true);
        probe.IsFirst.Set(false);
        reconciler.Flush();

        Assert.Equal(["unmount first.2", "unmount first.1", "mount second"], calls);

        mount.Unmount();
    }

    [Fact]
    public void DeeplyNestedComponents_MountAndUnmountWithoutAnyProvisionedStack()
    {
        const int depth = 100_000;
        RunOnDedicatedStack(256 * 1024, () => {
            using var world = new World();
            var mount = world.Mount(BuildNestedComponent(depth), new DeepNestProps(0, depth));

            var leaf = Assert.Single(helpers.FindAll<DeepNestValue>(world));
            Assert.Equal(depth, leaf.Get<DeepNestValue>().Value);

            mount.Unmount();
            Assert.Equal(0, world.Count);
        });
    }

    [Fact]
    public void ForEachOfModeratelyDeepItems_TearsDownWithoutRecursing()
    {
        const int itemDepth = 60;
        const int itemCount = 25;
        using var world = new World();
        var component = BuildNestedComponent(itemDepth);

        var mount = world.Mount(
            (in Unit _, ref Hooks _) => Reactive.ForEach<int, ComponentTerm<DeepNestProps>>(
                [.. Enumerable.Range(0, itemCount)
                    .Select(i => (i, Reactive.Component(component, new DeepNestProps(0, itemDepth))))]),
            default(Unit));

        Assert.Equal(itemCount, helpers.FindAll<DeepNestValue>(world).Length);

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void ForEach_WithManyShallowSiblings_MountsAndUnmountsCleanly()
    {
        const int itemCount = 1000;
        using var world = new World();

        var mount = world.Mount(
            (in Unit _, ref Hooks _) => Reactive.ForEach<int, DeepNestValue, EntityTerm<HList<DeepNestValue, EmptyHList>, UnitTerm>>(
                static (in DeepNestValue item) => Reactive.Entity(HList.From(item)),
                [.. Enumerable.Range(0, itemCount).Select(i => (i, new DeepNestValue(i)))]),
            default(Unit));

        Assert.Equal(itemCount, helpers.FindAll<DeepNestValue>(world).Length);

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void NestedComponentSiblings_ExpandInPreOrderDepthFirstSequence()
    {
        var calls = new List<string>();
        using var world = new World();
        ReactiveComponent<LeafName> leaf = (in LeafName name, ref Hooks _)
            => new ReactiveNode<EffectTerm<OrderLogEffect>>(
                Term.Effect(new OrderLogEffect(calls, name.Value)));

        var mount = world.Mount(
            (in Unit _, ref Hooks _) => Reactive.Group(
                Reactive.Component(
                    (in Unit _, ref Hooks _) => Reactive.Group(
                        Reactive.Component(leaf, new LeafName("X1")),
                        Reactive.Component(leaf, new LeafName("X2"))),
                    default(Unit)),
                Reactive.Component(leaf, new LeafName("Y"))),
            default(Unit));

        Assert.Equal(["X1", "X2", "Y"], calls);

        mount.Unmount();
    }

    [Fact]
    public void FirstTimeDiscoveredChildFailure_DuringAStandaloneReconcile_LeavesTheMountUsable()
    {
        using var world = new World();
        var mount = world.Mount(
            (in Unit _, ref Hooks hooks) => {
                var reveal = hooks.UseState(false);
                return Reactive.Entity(
                    HList.From(new BoundaryMarker()),
                    Reactive.Group(
                        Reactive.On(
                            reveal,
                            static (in RevealEvent _, in State<bool> state) => state.Set(true)),
                        Reactive.When(
                            reveal.Value,
                            new ReactiveNode<LiftTerm<AlwaysThrowsSpec>>(
                                Term.Lift(default(AlwaysThrowsSpec))))));
            },
            default(Unit));

        var marker = Assert.Single(helpers.FindAll<BoundaryMarker>(world));

        world.Send(marker, new RevealEvent());
        var error = Assert.Throws<InvalidOperationException>(world.FlushReactive);
        Assert.Equal("first-time expansion failed", error.Message);

        Assert.True(mount.IsMounted);
        Assert.Equal(marker, Assert.Single(helpers.FindAll<BoundaryMarker>(world)));

        mount.Unmount();
    }

    [Fact]
    public void FirstTimeDiscoveredChildFailure_ThenAnUnrelatedParentReRender_RetriesTheZombieChild()
    {
        using var world = new World();
        var probe = new FlakyChildProbe();
        var mount = world.Mount(
            (in Unit _, ref Hooks hooks) => {
                var reveal = hooks.UseState(false);
                var unrelated = hooks.UseState(0);
                return Reactive.Entity(
                    HList.From(new BoundaryMarker(), new UnrelatedCounter(unrelated.Value)),
                    Reactive.Group(
                        Reactive.On(
                            reveal,
                            static (in RevealEvent _, in State<bool> state) => state.Set(true)),
                        Reactive.On(
                            unrelated,
                            static (in BumpUnrelatedEvent _, in State<int> state) =>
                                state.Set(state.Value + 1)),
                        Reactive.When(
                            reveal.Value,
                            new ReactiveNode<LiftTerm<FlakyChildSpec>>(
                                Term.Lift(new FlakyChildSpec(probe))))));
            },
            default(Unit));

        var marker = Assert.Single(helpers.FindAll<BoundaryMarker>(world));

        world.Send(marker, new RevealEvent());
        var error = Assert.Throws<InvalidOperationException>(world.FlushReactive);
        Assert.Equal("flaky child failed", error.Message);

        Assert.Empty(helpers.FindAll<FlakyChildMarker>(world));
        Assert.True(mount.IsMounted);
        Assert.Equal(0, probe.MountCount);

        probe.ShouldThrow = false;
        world.Send(marker, new BumpUnrelatedEvent());
        world.FlushReactive();

        Assert.Equal(1, probe.MountCount);
        Assert.Single(helpers.FindAll<FlakyChildMarker>(world));

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void ReentrantUnmountDuringAnInProgressDestroyWalk_DoesNotCorruptTheOuterWalk()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var calls = new List<string>();

        var mountB = world.Mount(static (in Unit _, ref Hooks _) => Reactive.None, default(Unit));
        var specA = new ReentrantTeardownSpec(calls, mountB);
        var mountA = reconciler.Mount(specA);

        Assert.Equal(["mount sibling", "mount reentrant"], calls);
        calls.Clear();

        mountA.Unmount();

        Assert.Equal(["unmount reentrant", "unmount sibling"], calls);
        Assert.False(mountB.IsMounted);
        Assert.Equal(0, world.Count);
    }

    private static void RunOnDedicatedStack(int stackSizeBytes, Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                body();
            }
            catch (Exception error) {
                failure = error;
            }
        }, stackSizeBytes);
        thread.Start();
        thread.Join();

        if (failure != null) {
            throw failure;
        }
    }

    private static ReactiveComponent<DeepNestProps> BuildNestedComponent(int depth)
    {
        ReactiveComponent<DeepNestProps> component = null!;
        component = (in DeepNestProps props, ref Hooks _) =>
            props.Level >= props.Depth
                ? Reactive.Entity(HList.From(new DeepNestValue(props.Level)))
                : Reactive.Component(component, new DeepNestProps(props.Level + 1, props.Depth));
        return component;
    }

    [Fact]
    public void ContextShadowing_NestedProvideOfSameType_ResolvesNearestScope()
    {
        using var world = new World();
        var mount = world.Mount(
            static (in Unit _, ref Hooks hooks) => {
                var outer = hooks.UseState(1);
                var inner = hooks.UseState(2);
                return Reactive.Entity(
                    HList.From(new OuterMarker()),
                    Reactive.Group(
                        Reactive.On(
                            outer,
                            static (in SetOuter message, in State<int> state) =>
                                state.Set(message.Value)),
                        Reactive.On(
                            inner,
                            static (in SetInner message, in State<int> state) =>
                                state.Set(message.Value)),
                        Reactive.Provide(
                            new ShadowContext(outer.Value),
                            Reactive.Provide(
                                new ShadowContext(inner.Value),
                                Reactive.Use<
                                    ShadowContext,
                                    EntityTerm<HList<ShadowValue, EmptyHList>, UnitTerm>>(
                                    static (in ShadowContext value) =>
                                        Reactive.Entity(HList.From(
                                            new ShadowValue(value.Value))))))));
            },
            default(Unit));

        var outerMarker = Assert.Single(helpers.FindAll<OuterMarker>(world));
        Assert.Equal(2, Assert.Single(helpers.FindAll<ShadowValue>(world)).Get<ShadowValue>().Value);

        world.Send(outerMarker, new SetInner(20));
        world.FlushReactive();
        Assert.Equal(20, Assert.Single(helpers.FindAll<ShadowValue>(world)).Get<ShadowValue>().Value);

        world.Send(outerMarker, new SetOuter(999));
        world.FlushReactive();
        Assert.Equal(20, Assert.Single(helpers.FindAll<ShadowValue>(world)).Get<ShadowValue>().Value);

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void NestedFlushDuringExpansion_IsASilentNoOp()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var flushedDuringExpand = false;
        var probe = new NestedFlushProbe();
        probe.DuringExpand = () => {
            reconciler.Flush();
            flushedDuringExpand = true;
        };
        var mount = reconciler.Mount(new NestedFlushSpec(probe));

        Assert.True(flushedDuringExpand);
        Assert.Single(helpers.FindAll<BoundaryMarker>(world));

        mount.Unmount();
    }

    [Fact]
    public void MountingANewRootDuringAnotherRootsExpansion_DefersAndSettlesWithinTheSameFlush()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        MountHandle<HandleSpec>? inner = null;
        var probe = new NestedFlushProbe();
        probe.DuringExpand = () => inner = reconciler.Mount(new HandleSpec(1));

        var mount = reconciler.Mount(new NestedFlushSpec(probe));

        Assert.NotNull(inner);
        Assert.True(inner.Value.IsMounted);
        Assert.Equal(1, helpers.FindSingle<ReactiveValue>(world).Get<ReactiveValue>().Value);
        Assert.Single(helpers.FindAll<BoundaryMarker>(world));

        inner.Value.Unmount();
        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void HookTypeChange_AtTheSameIndex_ThrowsInsteadOfCorruptingState()
    {
        using var world = new World();
        var useBool = false;
        var mount = world.Mount(
            (in Unit _, ref Hooks hooks) => {
                if (useBool) {
                    hooks.UseState(false);
                }
                else {
                    hooks.UseState(0);
                }
                return Reactive.None;
            },
            default(Unit));

        useBool = true;
        mount.Invalidate();

        var error = Assert.Throws<InvalidOperationException>(world.FlushReactive);
        Assert.Contains("previously a different type", error.Message);

        mount.Unmount();
    }

    [Fact]
    public void ForEach_DuplicateKeyOnInitialMount_RollsBackEarlierSiblingOutputsToo()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();

        Assert.Throws<InvalidOperationException>(
            () => reconciler.Mount(new SiblingThenDuplicateForEachSpec()));

        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void ForEach_EmptyThenPopulatedThenEmptyAgain_LeavesNoResidualChildren()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var mount = reconciler.Mount(new ListSpec(ReadOnlyMemory<KeyedValue>.Empty));

        Assert.Empty(helpers.FindAll<KeyedValue>(world));

        mount.Update(new ListSpec(new KeyedValue[] { new(1, 10), new(2, 20) }));
        reconciler.Flush();
        Assert.Equal(2, helpers.FindAll<KeyedValue>(world).Length);

        mount.Update(new ListSpec(ReadOnlyMemory<KeyedValue>.Empty));
        reconciler.Flush();
        Assert.Empty(helpers.FindAll<KeyedValue>(world));

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

}

public readonly record struct BoundaryMarker;
public readonly record struct RevealEvent : IEvent;
public readonly record struct BumpUnrelatedEvent : IEvent;
public readonly record struct UnrelatedCounter(int Value);

public sealed class FlakyChildProbe
{
    public bool ShouldThrow = true;
    public int MountCount;
}

public readonly record struct FlakyChildMarker;

public readonly record struct FlakyChildSpec(FlakyChildProbe Probe)
    : ISpec<FlakyChildSpec, Unit, EntityTerm<HList<FlakyChildMarker, EmptyHList>, UnitTerm>>
{
    public static EntityTerm<HList<FlakyChildMarker, EmptyHList>, UnitTerm> Expand(
        in FlakyChildSpec props, in Unit state, in ExpandContext context)
    {
        if (props.Probe.ShouldThrow) {
            throw new InvalidOperationException("flaky child failed");
        }
        props.Probe.MountCount++;
        return Term.Entity(HList.From(new FlakyChildMarker()));
    }
}

public readonly record struct LeafName(string Value);

public readonly record struct OrderLogEffect(List<string> Calls, string Name) : IEffect<OrderLogEffect>
{
    public static void Mount(in OrderLogEffect self) => self.Calls.Add(self.Name);
    public static void Reconcile(in OrderLogEffect previous, in OrderLogEffect next) {}
    public static void Unmount(in OrderLogEffect self) {}
}

public readonly record struct AlwaysThrowsSpec : ISpec<AlwaysThrowsSpec, Unit, UnitTerm>
{
    public static UnitTerm Expand(in AlwaysThrowsSpec props, in Unit state, in ExpandContext context)
        => throw new InvalidOperationException("first-time expansion failed");
}

public readonly record struct DeepNestProps(int Level, int Depth);
public readonly record struct DeepNestValue(int Value);

public readonly record struct SiblingTeardownEffect(List<string> Calls) : IEffect<SiblingTeardownEffect>
{
    public static void Mount(in SiblingTeardownEffect self) => self.Calls.Add("mount sibling");
    public static void Reconcile(in SiblingTeardownEffect previous, in SiblingTeardownEffect next) {}
    public static void Unmount(in SiblingTeardownEffect self) => self.Calls.Add("unmount sibling");
}

public readonly record struct ReentrantUnmountEffect(List<string> Calls, ReactiveMount<Unit> Target)
    : IEffect<ReentrantUnmountEffect>
{
    public static void Mount(in ReentrantUnmountEffect self) => self.Calls.Add("mount reentrant");
    public static void Reconcile(in ReentrantUnmountEffect previous, in ReentrantUnmountEffect next) {}

    public static void Unmount(in ReentrantUnmountEffect self)
    {
        self.Calls.Add("unmount reentrant");
        self.Target.Unmount();
    }
}

public readonly record struct ReentrantTeardownSpec(List<string> Calls, ReactiveMount<Unit> Target)
    : ISpec<ReentrantTeardownSpec, Unit,
        Group<EffectTerm<SiblingTeardownEffect>, EffectTerm<ReentrantUnmountEffect>>>
{
    public static Group<EffectTerm<SiblingTeardownEffect>, EffectTerm<ReentrantUnmountEffect>> Expand(
        in ReentrantTeardownSpec props,
        in Unit state,
        in ExpandContext context)
        => Term.Group(
            Term.Effect(new SiblingTeardownEffect(props.Calls)),
            Term.Effect(new ReentrantUnmountEffect(props.Calls, props.Target)));
}

public sealed class FailingTeardownProbe
{
    public StateRef<bool> Visible;
}

public readonly record struct OkTeardownEffect(List<string> Calls) : IEffect<OkTeardownEffect>
{
    public static void Mount(in OkTeardownEffect self) => self.Calls.Add("mount ok");
    public static void Reconcile(in OkTeardownEffect previous, in OkTeardownEffect next) {}
    public static void Unmount(in OkTeardownEffect self) => self.Calls.Add("unmount ok");
}

public readonly record struct FailingTeardownEffect(List<string> Calls) : IEffect<FailingTeardownEffect>
{
    public static void Mount(in FailingTeardownEffect self) => self.Calls.Add("mount failing");
    public static void Reconcile(in FailingTeardownEffect previous, in FailingTeardownEffect next) {}

    public static void Unmount(in FailingTeardownEffect self)
    {
        self.Calls.Add("unmount failing");
        throw new InvalidOperationException("teardown failed");
    }
}

public readonly record struct FailingTeardownSpec(List<string> Calls, FailingTeardownProbe Probe)
    : ISpec<FailingTeardownSpec, bool,
        CondTerm<Group<EffectTerm<OkTeardownEffect>, EffectTerm<FailingTeardownEffect>>>>
{
    public static bool InitialState(in FailingTeardownSpec props) => true;

    public static CondTerm<Group<EffectTerm<OkTeardownEffect>, EffectTerm<FailingTeardownEffect>>> Expand(
        in FailingTeardownSpec props,
        in bool state,
        in ExpandContext context)
    {
        props.Probe.Visible = context.UseState<bool>();
        return Term.Cond(
            state,
            Term.Group(
                Term.Effect(new OkTeardownEffect(props.Calls)),
                Term.Effect(new FailingTeardownEffect(props.Calls))));
    }
}
public readonly record struct OuterMarker;
public readonly record struct ShadowValue(int Value);
public readonly record struct ShadowContext(int Value);
public readonly record struct SetOuter(int Value) : IEvent;
public readonly record struct SetInner(int Value) : IEvent;

public sealed class EitherProbe
{
    public StateRef<bool> IsFirst;
}

public readonly record struct EitherFirst1(List<string> Calls) : IEffect<EitherFirst1>
{
    public static void Mount(in EitherFirst1 self) => self.Calls.Add("mount first.1");
    public static void Reconcile(in EitherFirst1 previous, in EitherFirst1 next) {}
    public static void Unmount(in EitherFirst1 self) => self.Calls.Add("unmount first.1");
}

public readonly record struct EitherFirst2(List<string> Calls) : IEffect<EitherFirst2>
{
    public static void Mount(in EitherFirst2 self) => self.Calls.Add("mount first.2");
    public static void Reconcile(in EitherFirst2 previous, in EitherFirst2 next) {}
    public static void Unmount(in EitherFirst2 self) => self.Calls.Add("unmount first.2");
}

public readonly record struct EitherSecond(List<string> Calls) : IEffect<EitherSecond>
{
    public static void Mount(in EitherSecond self) => self.Calls.Add("mount second");
    public static void Reconcile(in EitherSecond previous, in EitherSecond next) {}
    public static void Unmount(in EitherSecond self) => self.Calls.Add("unmount second");
}

public readonly record struct EitherSpec(List<string> Calls, EitherProbe Probe)
    : ISpec<EitherSpec, bool,
        EitherTerm<Group<EffectTerm<EitherFirst1>, EffectTerm<EitherFirst2>>, EffectTerm<EitherSecond>>>
{
    public static bool InitialState(in EitherSpec props) => true;

    public static EitherTerm<Group<EffectTerm<EitherFirst1>, EffectTerm<EitherFirst2>>, EffectTerm<EitherSecond>>
        Expand(in EitherSpec props, in bool state, in ExpandContext context)
    {
        props.Probe.IsFirst = context.UseState<bool>();
        return Term.Either(
            state,
            Term.Group(
                Term.Effect(new EitherFirst1(props.Calls)),
                Term.Effect(new EitherFirst2(props.Calls))),
            Term.Effect(new EitherSecond(props.Calls)));
    }
}

public sealed class NestedFlushProbe
{
    public Action? DuringExpand;
}

public readonly record struct NestedFlushSpec(NestedFlushProbe Probe)
    : ISpec<NestedFlushSpec, Unit, EntityTerm<MarkerList, UnitTerm>>
{
    public static EntityTerm<MarkerList, UnitTerm> Expand(
        in NestedFlushSpec props,
        in Unit state,
        in ExpandContext context)
    {
        props.Probe.DuringExpand?.Invoke();
        return Term.Entity(HList.From(new BoundaryMarker()));
    }
}

public readonly record struct SiblingThenDuplicateForEachSpec
    : ISpec<SiblingThenDuplicateForEachSpec, Unit,
        Group<EntityTerm<HList<KeyedValue, EmptyHList>, UnitTerm>, ForEachTerm<int, ItemSpec>>>
{
    public static Group<EntityTerm<HList<KeyedValue, EmptyHList>, UnitTerm>, ForEachTerm<int, ItemSpec>>
        Expand(in SiblingThenDuplicateForEachSpec props, in Unit state, in ExpandContext context)
        => Term.Group(
            Term.Entity(HList.From(new KeyedValue(0, 0))),
            Term.ForEach<int, ItemSpec>(new Keyed<int, ItemSpec>[] {
                Term.Keyed(1, new ItemSpec(new KeyedValue(1, 1))),
                Term.Keyed(1, new ItemSpec(new KeyedValue(1, 2))),
            }));
}
