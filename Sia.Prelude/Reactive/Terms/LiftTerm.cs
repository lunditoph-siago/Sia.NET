namespace Sia.Reactive;

public readonly record struct LiftTerm<TSpec>(TSpec Props) : ITerm<LiftTerm<TSpec>>
    where TSpec : struct, ISpec, IEquatable<TSpec>
{
    public static int SlotCount => 1;

    public static void Mount(in LiftTerm<TSpec> self, ref GraphContext ctx)
        => ctx.SetSlot(ctx.Reconciler.MountSub(
            self.Props,
            ctx.Cell,
            ctx.Depth + 1,
            ctx.NextSlotIndex,
            ctx.Schedule,
            ctx.Scope,
            ctx.Output));

    public static void Reconcile(
        in LiftTerm<TSpec> prev, in LiftTerm<TSpec> next, ref GraphContext ctx)
    {
        var sub = ctx.PeekSlot();
        if (sub is not { IsValid: true } cell) {
            ctx.SetSlot(ctx.Reconciler.MountSub(
                next.Props,
                ctx.Cell,
                ctx.Depth + 1,
                ctx.NextSlotIndex,
                ctx.Schedule,
                ctx.Scope,
                ctx.Output));
            return;
        }
        if (!prev.Props.Equals(next.Props)) {
            cell.GetUnchecked<TSpec>() = next.Props;
            ctx.Reconciler.EnqueueDirty(cell);
        }
        ctx.Advance();
    }
}
