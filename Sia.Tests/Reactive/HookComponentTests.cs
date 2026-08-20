namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

public class HookComponentTests(QueryTestHelpers helpers) : IClassFixture<QueryTestHelpers>
{
    [Fact]
    public void StatelessComponent_DoesNotAllocateHookStorage()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        var node = Reactive.Component(
            static (in Unit _, ref Hooks _) => Reactive.None,
            default(Unit));
        var mount = reconciler.Mount(
            new ReactiveNodeSpec<ComponentTerm<Unit>>(node.Term));
        var component = mount.Cell.Get<Cell>().Slots[0].Entity;

        Assert.Null(component.Get<Cell>().States);

        mount.Unmount();
    }

    [Fact]
    public void StateEffectAndEvent_ReconcileAndUnmountTogether()
    {
        using var world = new World();
        var probe = new HookProbe();
        ReactiveComponent<HookCounterProps> component =
            static (in HookCounterProps props, ref Hooks hooks) =>
            {
                var count = hooks.UseState(props.Initial);
                hooks.UseEffect(
                    new HookEffectDependencies(props.Probe, count.Value),
                    static (in HookEffectDependencies dependencies) =>
                    {
                        dependencies.Probe.Events.Add(
                            $"setup:{dependencies.Value}");
                        return new HookEffectResource(
                            dependencies.Probe,
                            dependencies.Value);
                    },
                    static (in HookEffectResource resource) =>
                        resource.Probe.Events.Add($"cleanup:{resource.Value}"));

                return Reactive.Entity(
                    HList.From(new HookCounterValue(count.Value)),
                    Reactive.On(
                        count,
                        static (in HookClick _, in State<int> state) =>
                            state.Set(state.Value + 1)));
            };

        var mount = world.Mount(component, new HookCounterProps(2, probe));
        var entity = helpers.FindSingle<HookCounterValue>(world);

        Assert.Equal(2, entity.Get<HookCounterValue>().Value);
        Assert.Equal(["setup:2"], probe.Events);

        world.Send(entity, new HookClick());
        world.FlushReactive();

        Assert.Equal(3, entity.Get<HookCounterValue>().Value);
        Assert.Equal(
            ["setup:2", "cleanup:2", "setup:3"],
            probe.Events);

        mount.Update(new HookCounterProps(100, probe));
        world.FlushReactive();

        Assert.Equal(3, entity.Get<HookCounterValue>().Value);
        Assert.Equal(3, probe.Events.Count);

        mount.Unmount();

        Assert.Equal(
            ["setup:2", "cleanup:2", "setup:3", "cleanup:3"],
            probe.Events);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void AddingFirstHookAfterInitialRender_IsRejected()
    {
        using var world = new World();
        ReactiveComponent<bool> component =
            static (in bool enabled, ref Hooks hooks) =>
            {
                if (enabled) {
                    _ = hooks.UseState(0);
                }
                return Reactive.None;
            };
        var mount = world.Mount(component, false);

        mount.Update(true);

        var error = Assert.Throws<InvalidOperationException>(
            world.FlushReactive);
        Assert.Contains("Hook count changed", error.Message);

        mount.Unmount();
    }

    [Fact]
    public void GeneratedHookComponent_CanBeMounted()
    {
        using var world = new World();
        var mount = world.Mount(
            GeneratedHookCounter.Definition,
            new GeneratedHookCounterProps(7));
        var entity = Assert.Single<GeneratedHookCounterValue>(
            helpers.FindAll<GeneratedHookCounterValue>(world)
                .Select(static item => item.Get<GeneratedHookCounterValue>()));

        Assert.Equal(7, entity.Value);

        mount.Unmount();
        Assert.Equal(0, world.Count);
    }
}

[ReactiveComponent]
public static partial class GeneratedHookCounter
{
    public static ReactiveNode Render(
        in GeneratedHookCounterProps props,
        ref Hooks hooks)
    {
        var count = hooks.UseState(props.Initial);
        return Reactive.Entity(
            HList.From(new GeneratedHookCounterValue(count.Value)));
    }
}

public sealed class HookProbe
{
    public List<string> Events { get; } = [];
}

public readonly record struct HookCounterProps(int Initial, HookProbe Probe);
public readonly record struct HookCounterValue(int Value);
public readonly record struct HookClick : IEvent;
public readonly record struct HookEffectDependencies(HookProbe Probe, int Value);
public readonly record struct HookEffectResource(HookProbe Probe, int Value);
public readonly record struct GeneratedHookCounterProps(int Initial);
public readonly record struct GeneratedHookCounterValue(int Value);
