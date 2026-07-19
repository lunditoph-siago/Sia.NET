namespace Sia.Reactive;

public readonly record struct SystemTerm<TSystem> : ITerm<SystemTerm<TSystem>>
    where TSystem : ISystem, new()
{
    public static int SlotCount => 1;

    public static void Mount(in SystemTerm<TSystem> self, ref GraphContext ctx)
        => ctx.SetSlot(ctx.Reconciler.RegisterSystem<TSystem>(
            ctx.Schedule, ctx.Cell, ctx.NextSlotIndex));

    public static void Reconcile(
        in SystemTerm<TSystem> prev, in SystemTerm<TSystem> next, ref GraphContext ctx)
    {
        if (ctx.PeekSlot() is { IsValid: true }) {
            ctx.Advance();
        }
        else {
            Mount(next, ref ctx);
        }
    }
}

public readonly record struct ScheduleTerm<TLabel, TChildren>(TChildren Children)
    : ITerm<ScheduleTerm<TLabel, TChildren>>
    where TLabel : struct
    where TChildren : struct, ITerm<TChildren>
{
    public static int SlotCount => 1 + TChildren.SlotCount;

    public static void Mount(in ScheduleTerm<TLabel, TChildren> self, ref GraphContext ctx)
    {
        var (registry, node) = ctx.Reconciler.CreateSchedule(typeof(TLabel));
        ctx.SetSlot(node);

        var saved = ctx.Schedule;
        ctx.Schedule = registry;
        try {
            TChildren.Mount(self.Children, ref ctx);
        }
        finally {
            ctx.Schedule = saved;
        }
    }

    public static void Reconcile(
        in ScheduleTerm<TLabel, TChildren> prev, in ScheduleTerm<TLabel, TChildren> next,
        ref GraphContext ctx)
    {
        var slot = ctx.PeekSlot();
        if (slot is not { IsValid: true } node) {
            Mount(next, ref ctx);
            return;
        }
        ctx.Advance();

        var saved = ctx.Schedule;
        ctx.Schedule = node.GetUnchecked<ScheduleNode>().Registry;
        try {
            TChildren.Reconcile(prev.Children, next.Children, ref ctx);
        }
        finally {
            ctx.Schedule = saved;
        }
    }
}
