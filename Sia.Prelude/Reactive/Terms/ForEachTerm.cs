namespace Sia.Reactive;

public readonly record struct Keyed<TKey, TSpec>(TKey Key, TSpec Props)
    where TKey : notnull
    where TSpec : struct, ISpec, IEquatable<TSpec>;

internal interface IForEachCleanup
{
    void DestroyChildren(Reconciler reconciler);
}

public struct EachNode
{
    internal IForEachCleanup Cleanup;
}

internal sealed class EachIndex<TKey> : IForEachCleanup
    where TKey : notnull
{
    internal struct Entry
    {
        public Entity Cell;
        public int Stamp;
    }

    public readonly Dictionary<TKey, Entry> ByKey = [];
    public int Stamp;

    private readonly List<TKey> _staleKeys = [];

    public void DestroyChildren(Reconciler reconciler)
    {
        foreach (var entry in ByKey.Values) {
            if (entry.Cell.IsValid) {
                reconciler.DestroySlot(entry.Cell);
            }
        }
        ByKey.Clear();
    }

    public void RemoveStale(Reconciler reconciler, int stamp)
    {
        var staleKeys = _staleKeys;
        foreach (var (key, entry) in ByKey) {
            if (entry.Stamp != stamp) {
                staleKeys.Add(key);
            }
        }
        foreach (var key in staleKeys) {
            var cell = ByKey[key].Cell;
            ByKey.Remove(key);
            if (cell.IsValid) {
                reconciler.DestroySlot(cell);
            }
        }
        staleKeys.Clear();
    }
}

public readonly record struct ForEachTerm<TKey, TSpec>(ReadOnlyMemory<Keyed<TKey, TSpec>> Items)
    : ITerm<ForEachTerm<TKey, TSpec>>
    where TKey : notnull
    where TSpec : struct, ISpec, IEquatable<TSpec>
{
    public static int SlotCount => 1;

    public static void Mount(in ForEachTerm<TKey, TSpec> self, ref GraphContext ctx)
    {
        var slotIndex = ctx.NextSlotIndex;
        var index = new EachIndex<TKey>();
        ctx.SetSlot(ctx.Reconciler.CreateNode(new EachNode { Cleanup = index }));
        Upsert(index, self.Items.Span, slotIndex, ref ctx);
    }

    public static void Reconcile(
        in ForEachTerm<TKey, TSpec> prev, in ForEachTerm<TKey, TSpec> next,
        ref GraphContext ctx)
    {
        var slot = ctx.PeekSlot();
        if (slot is not { IsValid: true }) {
            Mount(next, ref ctx);
            return;
        }

        var slotIndex = ctx.NextSlotIndex;
        var index = (EachIndex<TKey>)slot.Get<EachNode>().Cleanup;
        var stamp = Upsert(index, next.Items.Span, slotIndex, ref ctx);
        index.RemoveStale(ctx.Reconciler, stamp);
        ctx.Advance();
    }

    private static int Upsert(
        EachIndex<TKey> index, ReadOnlySpan<Keyed<TKey, TSpec>> items, int slotIndex,
        ref GraphContext ctx)
    {
        var stamp = ++index.Stamp;
        var byKey = index.ByKey;
        foreach (ref readonly var item in items) {
            if (byKey.TryGetValue(item.Key, out var entry) && entry.Cell.IsValid) {
                if (!entry.Cell.Get<TSpec>().Equals(item.Props)) {
                    entry.Cell.Get<TSpec>() = item.Props;
                    ctx.Reconciler.EnqueueDirty(entry.Cell);
                }
            }
            else {
                entry.Cell = ctx.Reconciler.MountSub(
                    item.Props, ctx.Cell, ctx.Depth + 1, slotIndex, ctx.Schedule, ctx.Scope);
            }
            entry.Stamp = stamp;
            byKey[item.Key] = entry;
        }
        return stamp;
    }
}
