namespace Sia.Reactive;

public readonly record struct DeferredEffectTerm<TDependencies, TResource>(
    TDependencies Dependencies,
    ReactiveEffectSetup<TDependencies, TResource> Setup,
    ReactiveEffectCleanup<TResource> Cleanup)
    : ITerm<DeferredEffectTerm<TDependencies, TResource>>
    where TDependencies : struct, IEquatable<TDependencies>
{
    public static int SlotCount => 1;

    public static void Mount(
        in DeferredEffectTerm<TDependencies, TResource> self,
        ref GraphContext context)
    {
        var reconciler = context.Reconciler;
        var state = new EffectLifetime<TDependencies, TResource>(
            reconciler,
            self.Dependencies,
            self.Setup,
            self.Cleanup);
        var node = reconciler.CreateNode(new EffectNode(state));
        try {
            state.ScheduleSetup();
            context.SetSlot(node);
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(() => reconciler.DestroySlot(node))
                .ThrowFailure();
        }
    }

    public static void Reconcile(
        in DeferredEffectTerm<TDependencies, TResource> previous,
        in DeferredEffectTerm<TDependencies, TResource> next,
        ref GraphContext context)
    {
        var node = context.PeekSlot();
        if (node is not { IsValid: true }) {
            Mount(next, ref context);
            return;
        }

        var state = (EffectLifetime<TDependencies, TResource>)
            node.GetUnchecked<EffectNode>().Cleanup;
        state.Reconcile(next.Dependencies, next.Setup, next.Cleanup);
        context.Advance();
    }
}
