namespace Sia.Reactive;

public readonly record struct FnLiftTerm<TProps>(Spec<TProps> Spec, TProps Props)
    : ITerm<FnLiftTerm<TProps>>
    where TProps : struct, IEquatable<TProps>
{
    public static int SlotCount => 1;

    public static void Mount(in FnLiftTerm<TProps> self, ref GraphContext ctx)
        => ctx.SetSlot(self.Spec.MountCell(
            ctx.Reconciler, self.Props, ctx.Cell, ctx.Depth + 1, ctx.NextSlotIndex,
            ctx.Schedule, ctx.Scope));

    public static void Reconcile(
        in FnLiftTerm<TProps> prev, in FnLiftTerm<TProps> next,
        ref GraphContext ctx)
    {
        var sub = ctx.PeekSlot();
        if (sub is not { IsValid: true }) {
            Mount(next, ref ctx);
            return;
        }
        if (!ReferenceEquals(prev.Spec, next.Spec)) {
            ctx.Reconciler.DestroySlot(sub);
            Mount(next, ref ctx);
            return;
        }
        if (!prev.Props.Equals(next.Props)) {
            sub.Get<TProps>() = next.Props;
            ctx.Reconciler.EnqueueDirty(sub);
        }
        ctx.Advance();
    }
}
