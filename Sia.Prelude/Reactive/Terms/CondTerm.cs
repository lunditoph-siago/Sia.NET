namespace Sia.Reactive;

public readonly record struct CondTerm<TTerm>(bool Condition, TTerm Term)
    : ITerm<CondTerm<TTerm>>
    where TTerm : struct, ITerm<TTerm>
{
    public static int SlotCount => TTerm.SlotCount;

    public static void Mount(in CondTerm<TTerm> self, ref GraphContext ctx)
    {
        if (self.Condition) {
            TTerm.Mount(self.Term, ref ctx);
        }
        else {
            ctx.Skip(TTerm.SlotCount);
        }
    }

    public static void Reconcile(
        in CondTerm<TTerm> prev, in CondTerm<TTerm> next, ref GraphContext ctx)
    {
        if (prev.Condition) {
            if (next.Condition) {
                if (TTerm.SlotCount > 0 && !ctx.PeekSlot().IsValid) {
                    ctx.RemountRange(TTerm.SlotCount);
                    Mount(next, ref ctx);
                    return;
                }
                TTerm.Reconcile(prev.Term, next.Term, ref ctx);
            }
            else {
                ctx.DestroyRange(TTerm.SlotCount);
            }
        }
        else if (next.Condition) {
            TTerm.Mount(next.Term, ref ctx);
        }
        else {
            ctx.Skip(TTerm.SlotCount);
        }
    }
}

public readonly record struct EitherTerm<TFirst, TSecond>(
    bool IsFirst, TFirst First, TSecond Second)
    : ITerm<EitherTerm<TFirst, TSecond>>
    where TFirst : struct, ITerm<TFirst>
    where TSecond : struct, ITerm<TSecond>
{
    public static int SlotCount => TFirst.SlotCount + TSecond.SlotCount;

    public static void Mount(in EitherTerm<TFirst, TSecond> self, ref GraphContext ctx)
    {
        if (self.IsFirst) {
            TFirst.Mount(self.First, ref ctx);
            ctx.Skip(TSecond.SlotCount);
        }
        else {
            ctx.Skip(TFirst.SlotCount);
            TSecond.Mount(self.Second, ref ctx);
        }
    }

    public static void Reconcile(
        in EitherTerm<TFirst, TSecond> prev, in EitherTerm<TFirst, TSecond> next,
        ref GraphContext ctx)
    {
        if (prev.IsFirst) {
            if (next.IsFirst) {
                TFirst.Reconcile(prev.First, next.First, ref ctx);
                ctx.Skip(TSecond.SlotCount);
            }
            else {
                ctx.DestroyRange(TFirst.SlotCount);
                TSecond.Mount(next.Second, ref ctx);
            }
        }
        else if (next.IsFirst) {
            var start = ctx.NextSlotIndex;
            ctx.Skip(TFirst.SlotCount);
            ctx.DestroyRange(TSecond.SlotCount);
            ctx.RewindTo(start);
            TFirst.Mount(next.First, ref ctx);
            ctx.Skip(TSecond.SlotCount);
        }
        else {
            ctx.Skip(TFirst.SlotCount);
            TSecond.Reconcile(prev.Second, next.Second, ref ctx);
        }
    }
}
