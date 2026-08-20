namespace Sia.Tests.Reactive;

using global::Sia.Reactive;

public class ReactiveComponentRegressionTests
{
    [Fact]
    public void DeferredEffect_SetupSurvivesALaterReconciliationFailure()
    {
        using var world = new World();
        var calls = new List<string>();

        var effectMount = world.Mount(
            (in int dependency, ref Hooks hooks) => {
                hooks.UseEffect(
                    dependency,
                    (in int value) => {
                        calls.Add($"setup {value}");
                        return value;
                    },
                    (in int resource) => calls.Add($"cleanup {resource}"));
                return Reactive.None;
            },
            0);

        var failureMount = world.Mount(
            static (in bool shouldThrow, ref Hooks _) =>
                new ReactiveNode<RegressionThrowTerm>(new(shouldThrow)),
            false);

        Assert.Equal(["setup 0"], calls);

        effectMount.Update(1);
        failureMount.Update(true);

        var error = Assert.Throws<InvalidOperationException>(world.FlushReactive);
        Assert.Equal("reconciliation failed", error.Message);
        Assert.Equal(["setup 0", "cleanup 0"], calls);

        world.FlushReactive();

        Assert.Equal(["setup 0", "cleanup 0", "setup 1"], calls);

        failureMount.Unmount();
        effectMount.Unmount();
        Assert.Equal("cleanup 1", calls[^1]);
    }

    [Fact]
    public void ComponentRenderer_CannotUnmountItsExpandingRoot()
    {
        using var world = new World();
        ReactiveMount<bool> mount = default;

        ReactiveComponent<bool> component =
            (in bool shouldUnmount, ref Hooks _) => {
                if (shouldUnmount) {
                    mount.Unmount();
                }
                return Reactive.None;
            };
        mount = world.Mount(component, false);

        mount.Update(true);

        var error = Assert.Throws<InvalidOperationException>(world.FlushReactive);
        Assert.Contains("expanding", error.Message);
        Assert.True(mount.IsMounted);

        mount.Unmount();
    }

    [Fact]
    public void SynchronousEffect_CannotUnmountItsReconcilingRoot()
    {
        using var world = new World();
        ReactiveMount<bool> mount = default;
        Action noOp = static () => { };

        ReactiveComponent<bool> component =
            (in bool shouldUnmount, ref Hooks _) =>
                new ReactiveNode<EffectTerm<RegressionUnmountEffect>>(
                    Term.Effect(new RegressionUnmountEffect(
                        shouldUnmount ? mount.Unmount : noOp)));
        mount = world.Mount(component, false);

        mount.Update(true);

        var error = Assert.Throws<InvalidOperationException>(world.FlushReactive);
        Assert.Contains("reconciling", error.Message);
        Assert.True(mount.IsMounted);

        mount.Unmount();
    }
}

public readonly record struct RegressionThrowTerm(bool ShouldThrow)
    : ITerm<RegressionThrowTerm>
{
    public static int SlotCount => 0;

    public static void Mount(
        in RegressionThrowTerm self,
        ref GraphContext context)
    {
    }

    public static void Reconcile(
        in RegressionThrowTerm previous,
        in RegressionThrowTerm next,
        ref GraphContext context)
    {
        if (next.ShouldThrow) {
            throw new InvalidOperationException("reconciliation failed");
        }
    }
}

public readonly record struct RegressionUnmountEffect(Action Callback)
    : IEffect<RegressionUnmountEffect>
{
    public static void Mount(in RegressionUnmountEffect self)
        => self.Callback();

    public static void Reconcile(
        in RegressionUnmountEffect previous,
        in RegressionUnmountEffect next)
        => next.Callback();

    public static void Unmount(in RegressionUnmountEffect self)
    {
    }
}
