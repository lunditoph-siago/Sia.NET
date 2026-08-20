namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

using ScopedList = HList<ScopedChildValue, EmptyHList>;

public class ReactiveComponentIntegrationTests(QueryTestHelpers helpers) : IClassFixture<QueryTestHelpers>
{
    [Fact]
    public void NestedHookComponents_UpdateIndependently()
    {
        using var world = new World();
        var mount = world.Mount(
            static (in ParentProps props, ref Hooks hooks) => {
                var multiplier = hooks.UseState(1);
                return Reactive.Entity(
                    HList.From(new ParentMarker()),
                    Reactive.Group(
                        Reactive.On(
                            multiplier,
                            static (in SetMultiplier message, in State<int> state) =>
                                state.Set(message.Value)),
                        Reactive.Component<ChildProps>(
                            static (in ChildProps child, ref Hooks childHooks) => {
                                var bumps = childHooks.UseState(0);
                                return Reactive.Entity(
                                    HList.From(new ChildValue(child.Base + bumps.Value)),
                                    Reactive.On(
                                        bumps,
                                        static (in BumpEvent _, in State<int> state) =>
                                            state.Set(state.Value + 1)));
                            },
                            new(props.Seed * multiplier.Value))));
            },
            new ParentProps(10));

        var parent = Assert.Single(helpers.FindAll<ParentMarker>(world));
        var child = Assert.Single(helpers.FindAll<ChildValue>(world));
        Assert.Equal(10, child.Get<ChildValue>().Value);

        world.Send(child, new BumpEvent());
        world.Send(child, new BumpEvent());
        world.FlushReactive();
        Assert.Equal(12, child.Get<ChildValue>().Value);

        world.Send(parent, new SetMultiplier(3));
        world.FlushReactive();
        Assert.Equal(32, child.Get<ChildValue>().Value);

        world.Send(child, new BumpEvent());
        world.FlushReactive();
        Assert.Equal(33, child.Get<ChildValue>().Value);

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Theory]
    [InlineData(new[] { 4, 3, 2, 1, 0 })]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData(new[] { 1, 3 })]
    [InlineData(new[] { 3, 1, 5 })]
    public void ForEachOfHookComponents_SurvivesReorderAddRemove(int[] next)
    {
        using var world = new World();
        var mount = world.Mount(
            static (in ListProps props, ref Hooks _) =>
                Reactive.ForEach(BuildItems(props.Keys)),
            new ListProps([0, 1, 2, 3, 4]));

        mount.Update(new ListProps(next));
        world.FlushReactive();

        Assert.Equal(next.Length, helpers.FindAll<ChildValue>(world).Count());
        Assert.Equal(
            next.Select(key => key * 100).ToHashSet(),
            helpers.FindAll<ChildValue>(world)
                .Select(entity => entity.Get<ChildValue>().Value)
                .ToHashSet());

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void ForEachItem_HookStateSurvivesPropsOnlyReconcile()
    {
        using var world = new World();
        var mount = world.Mount(
            static (in int baseValue, ref Hooks _) => Reactive.ForEach(
                (1, Reactive.Component<ChildProps>(
                    static (in ChildProps child, ref Hooks hooks) => {
                        var bumps = hooks.UseState(0);
                        return Reactive.Entity(
                            HList.From(new ChildValue(child.Base + bumps.Value)),
                            Reactive.On(
                                bumps,
                                static (in BumpEvent _, in State<int> state) =>
                                    state.Set(state.Value + 1)));
                    },
                    new(baseValue)))),
            10);

        var entity = Assert.Single(helpers.FindAll<ChildValue>(world));
        world.Send(entity, new BumpEvent());
        world.Send(entity, new BumpEvent());
        world.FlushReactive();

        mount.Update(20);
        world.FlushReactive();

        Assert.Equal(entity, Assert.Single(helpers.FindAll<ChildValue>(world)));
        Assert.Equal(22, entity.Get<ChildValue>().Value);

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void ContextScope_PropagatesThroughNestedHookComponent()
    {
        using var world = new World();
        var mount = world.Mount(
            static (in Unit _, ref Hooks hooks) => {
                var theme = hooks.UseState(1);
                return Reactive.Entity(
                    HList.From(new ParentMarker()),
                    Reactive.Group(
                        Reactive.On(
                            theme,
                            static (in SetTheme message, in State<int> state) =>
                                state.Set(message.Value)),
                        Reactive.Provide(
                            new ThemeContext(theme.Value),
                            Reactive.Component<Unit>(
                                static (in Unit _, ref Hooks _) =>
                                    Reactive.Use<
                                        ThemeContext,
                                        EntityTerm<ScopedList, UnitTerm>>(
                                        static (in ThemeContext value) =>
                                            Reactive.Entity(HList.From(
                                                new ScopedChildValue(value.Value)))),
                                default))));
            },
            default(Unit));

        var parent = Assert.Single(helpers.FindAll<ParentMarker>(world));
        Assert.Equal(1, Assert.Single(helpers.FindAll<ScopedChildValue>(world))
            .Get<ScopedChildValue>().Value);

        world.Send(parent, new SetTheme(7));
        world.FlushReactive();
        Assert.Equal(7, Assert.Single(helpers.FindAll<ScopedChildValue>(world))
            .Get<ScopedChildValue>().Value);

        world.Send(parent, new SetTheme(9));
        world.Send(parent, new SetTheme(11));
        world.FlushReactive();
        Assert.Equal(11, Assert.Single(helpers.FindAll<ScopedChildValue>(world))
            .Get<ScopedChildValue>().Value);

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void HookEffect_TracksDependencyChangesInOrder()
    {
        using var world = new World();
        var calls = new List<string>();
        var mount = world.Mount(
            (in Unit _, ref Hooks hooks) => {
                var value = hooks.UseState(0);
                hooks.UseEffect(
                    value.Value,
                    (in int dependency) => {
                        calls.Add($"setup {dependency}");
                        return dependency;
                    },
                    (in int resource) => calls.Add($"cleanup {resource}"));
                return Reactive.Entity(
                    HList.From(new EffectControl()),
                    Reactive.On(
                        value,
                        static (in EffectDepsMsg message, in State<int> state) =>
                            state.Set(message.Value)));
            },
            default(Unit));
        var control = Assert.Single(helpers.FindAll<EffectControl>(world));

        for (var i = 1; i <= 3; i++) {
            world.Send(control, new EffectDepsMsg(i));
            world.FlushReactive();
        }
        mount.Unmount();

        Assert.Equal("setup 0", calls[0]);
        for (var i = 1; i <= 3; i++) {
            Assert.True(
                calls.IndexOf($"cleanup {i - 1}") < calls.IndexOf($"setup {i}"));
        }
        Assert.Equal("cleanup 3", calls[^1]);
    }

    [Fact]
    public void EventEffectAndSystem_CleanUpAcrossHookConditionToggles()
    {
        using var world = new World();
        var counter = world.Create(HList.From(new ToggleTickCounter()));
        var effectCalls = new List<string>();
        var mount = world.Mount(
            (in Unit _, ref Hooks hooks) => {
                var visible = hooks.UseState(true);
                return Reactive.Schedule(
                    default(ToggleSchedule),
                    Reactive.Group(
                        Reactive.System<ToggleTickSystem>(),
                        Reactive.Entity(
                            HList.From(new ToggleControl()),
                            Reactive.On(
                                visible,
                                static (in ToggleEvent _, in State<bool> state) =>
                                    state.Set(!state.Value))),
                        Reactive.When(
                            visible.Value,
                            Reactive.Entity(
                                HList.From(new ToggleMarker()),
                                Reactive.Group(
                                    Reactive.Effect(
                                        default(Unit),
                                        (in Unit _) => {
                                            effectCalls.Add("setup");
                                            return default(Unit);
                                        },
                                        (in Unit _) => effectCalls.Add("cleanup")),
                                    Reactive.On(
                                        visible,
                                        static (in BumpEvent _, in State<bool> state) =>
                                            state.Set(false)))))));
            },
            default(Unit));

        var control = Assert.Single(helpers.FindAll<ToggleControl>(world));
        for (var i = 0; i < 2; i++) {
            Assert.Equal(["setup"], effectCalls);
            effectCalls.Clear();
            world.Send(Assert.Single(helpers.FindAll<ToggleMarker>(world)), new BumpEvent());
            world.FlushReactive();
            Assert.Empty(helpers.FindAll<ToggleMarker>(world));
            Assert.Equal(["cleanup"], effectCalls);
            effectCalls.Clear();

            world.Send(control, new ToggleEvent());
            world.FlushReactive();
        }

        effectCalls.Clear();
        mount.Unmount();
        Assert.Equal(["cleanup"], effectCalls);
        Assert.Equal(counter, Assert.Single(helpers.FindAll<ToggleTickCounter>(world)));
    }

    [Fact]
    public void ThrowingHookEvent_DoesNotCorruptLaterUpdates()
    {
        using var world = new World();
        var mount = world.Mount(
            static (in Unit _, ref Hooks hooks) => {
                var value = hooks.UseState(1);
                return Reactive.Entity(
                    HList.From(new ChildValue(value.Value)),
                    Reactive.On(
                        value,
                        static (in ThrowingMsg message, in State<int> state) => {
                            if (message.ShouldThrow) {
                                throw new InvalidOperationException("boom");
                            }
                            state.Set(state.Value + message.Amount);
                        }));
            },
            default(Unit));
        var entity = Assert.Single(helpers.FindAll<ChildValue>(world));

        Assert.Throws<InvalidOperationException>(
            () => world.Send(entity, new ThrowingMsg(true, 0)));
        world.Send(entity, new ThrowingMsg(false, 5));
        world.FlushReactive();

        Assert.Equal(6, entity.Get<ChildValue>().Value);
        mount.Unmount();
    }

    [Fact]
    public void RepeatedMountUnmountOfHookTree_NeverLeaksEntities()
    {
        using var world = new World();
        for (var cycle = 0; cycle < 4; cycle++) {
            var mount = world.Mount(
                static (in Unit _, ref Hooks _) => Reactive.Entity(
                    HList.From(new ParentMarker()),
                    Reactive.ForEach(BuildItems([0, 1, 2, 3]))),
                default(Unit));

            Assert.Equal(5, world.Count);
            mount.Unmount();
            Assert.Equal(0, world.Count);
        }
    }

    private static (int Key, ReactiveNode<ComponentTerm<ChildProps>> Value)[]
        BuildItems(int[] keys)
        => [.. keys.Select(key => (
            key,
            Reactive.Component<ChildProps>(
                static (in ChildProps props, ref Hooks _) =>
                    Reactive.Entity(HList.From(new ChildValue(props.Base))),
                new(key * 100))))];
}

public readonly record struct ParentMarker;
public readonly record struct ChildValue(int Value);
public readonly record struct ScopedChildValue(int Value);
public readonly record struct ToggleMarker;
public readonly record struct ToggleControl;
public readonly record struct ToggleTickCounter;
public readonly record struct EffectControl;

public readonly record struct BumpEvent : IEvent;
public readonly record struct SetMultiplier(int Value) : IEvent;
public readonly record struct SetTheme(int Value) : IEvent;
public readonly record struct EffectDepsMsg(int Value) : IEvent;
public readonly record struct ToggleEvent : IEvent;
public readonly record struct ThrowingMsg(bool ShouldThrow, int Amount) : IEvent;

public readonly record struct ParentProps(int Seed);
public readonly record struct ChildProps(int Base);
public readonly record struct ListProps(int[] Keys);
public readonly record struct ThemeContext(int Value);
public readonly record struct ToggleSchedule;

public sealed class ToggleTickSystem() : SystemBase(Matchers.Of<ToggleTickCounter>());
