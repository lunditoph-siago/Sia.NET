namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

public class ReactiveFlushFixpointTests
{
    [Fact]
    public void MountingANewRootFromAnEffectSetup_CascadingEffectSettlesWithinTheSameFlush()
    {
        using var world = new World();
        var calls = new List<string>();
        MountHandle<EffectOnMountSpec>? inner = null;
        var mount = world.Mount(
            (in Unit _, ref Hooks hooks) => {
                hooks.UseEffect(
                    default(Unit),
                    (in Unit _) => {
                        var reconciler = world.AcquireAddon<Reconciler>();
                        inner = reconciler.Mount(new EffectOnMountSpec(calls));
                        return default(Unit);
                    },
                    static (in Unit _) => {});
                return Reactive.None;
            },
            default(Unit));

        Assert.NotNull(inner);
        Assert.True(inner.Value.IsMounted);
        Assert.Equal(["inner setup"], calls);

        world.FlushReactive();
        Assert.Equal(["inner setup"], calls);

        inner.Value.Unmount();
        mount.Unmount();
    }

    [Fact]
    public void EffectsThatKeepRetriggeringEachOther_FailFastInsteadOfHangingForever()
    {
        using var world = new World();
        var reconciler = world.AcquireAddon<Reconciler>();
        reconciler.MaxFlushPasses = 20;

        ReactiveComponent<Unit> component = (in Unit _, ref Hooks hooks) => {
            var ping = hooks.UseState(0);
            hooks.UseEffect(
                ping.Value,
                (in int value) => {
                    ping.Set(value + 1);
                    return value;
                },
                static (in int _) => {});
            return Reactive.None;
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => world.Mount(component, default(Unit)));
        Assert.Contains("did not settle", error.Message);

        Assert.Equal(0, world.Count);
        var recovery = world.Mount(
            static (in Unit _, ref Hooks _) => Reactive.None,
            default(Unit));
        Assert.True(recovery.IsMounted);
        recovery.Unmount();
    }
}

public readonly record struct EffectOnMountSpec(List<string> Calls)
    : ISpec<EffectOnMountSpec, Unit, DeferredEffectTerm<Unit, Unit>>
{
    public static DeferredEffectTerm<Unit, Unit> Expand(
        in EffectOnMountSpec props,
        in Unit state,
        in ExpandContext context)
    {
        var calls = props.Calls;
        return new(
            default,
            (in Unit _) => { calls.Add("inner setup"); return default; },
            static (in Unit _) => {});
    }
}
