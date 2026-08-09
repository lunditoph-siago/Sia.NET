namespace Sia.Reactive;

public readonly record struct PatchForEachTerm<TKey, TSpec>(
    ReadOnlyMemory<Keyed<TKey, TSpec>> Upserts,
    ReadOnlyMemory<TKey> Removals)
    : ITerm<PatchForEachTerm<TKey, TSpec>>
    where TKey : notnull
    where TSpec : struct, ISpec, IEquatable<TSpec>
{
    public static int SlotCount => 1;

    public static void Mount(in PatchForEachTerm<TKey, TSpec> self, ref GraphContext context)
    {
        var slotIndex = context.NextSlotIndex;
        var index = context.Reconciler.RentEachIndex<TKey>();
        context.SetSlot(context.Reconciler.CreateNode(new EachNode(index)));
        index.Patch(self.Upserts.Span, self.Removals.Span, slotIndex, ref context);
    }

    public static void Reconcile(
        in PatchForEachTerm<TKey, TSpec> previous,
        in PatchForEachTerm<TKey, TSpec> next,
        ref GraphContext context)
    {
        var slot = context.PeekSlot();
        if (slot is not { IsValid: true } eachNode) {
            Mount(next, ref context);
            return;
        }

        var slotIndex = context.NextSlotIndex;
        var index = (EachIndex<TKey>)eachNode.GetUnchecked<EachNode>().Cleanup;
        index.Patch(next.Upserts.Span, next.Removals.Span, slotIndex, ref context);
        context.Advance();
    }
}
