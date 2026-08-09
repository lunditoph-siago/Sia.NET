namespace Sia.Reactive;

public readonly record struct Keyed<TKey, TSpec>(TKey Key, TSpec Props)
    where TKey : notnull
    where TSpec : struct, ISpec, IEquatable<TSpec>;

public interface IForEachCleanup
{
    public void CollectChildren(List<Entity> destination);
}

internal interface IRecyclableForEachCleanup : IForEachCleanup
{
    public void Reset();
}

public readonly record struct EachNode(IForEachCleanup Cleanup);

public sealed class EachIndex<TKey> : IRecyclableForEachCleanup
    where TKey : notnull
{
    public struct Entry
    {
        public Entity Cell;
        public int Stamp;
    }

    public readonly Dictionary<TKey, Entry> ByKey = [];
    public int Stamp;

    private readonly List<TKey> _staleKeys = [];
    private readonly HashSet<TKey> _seenKeys = [];

    public void ValidateKeys<TSpec>(ReadOnlySpan<Keyed<TKey, TSpec>> items)
        where TSpec : struct, ISpec, IEquatable<TSpec>
    {
        try {
            foreach (ref readonly var item in items) {
                if (!_seenKeys.Add(item.Key)) {
                    throw new InvalidOperationException(
                        $"Duplicate key '{item.Key}' in Term.ForEach.");
                }
            }
        }
        finally {
            _seenKeys.Clear();
        }
    }

    internal int Upsert<TSpec>(
        ReadOnlySpan<Keyed<TKey, TSpec>> items,
        int slotIndex,
        ref GraphContext context)
        where TSpec : struct, ISpec, IEquatable<TSpec>
    {
        ValidateKeys(items);
        var stamp = ++Stamp;
        foreach (ref readonly var item in items) {
            UpsertItem(item, stamp, slotIndex, ref context);
        }
        return stamp;
    }

    internal void Patch<TSpec>(
        ReadOnlySpan<Keyed<TKey, TSpec>> upserts,
        ReadOnlySpan<TKey> removals,
        int slotIndex,
        ref GraphContext context)
        where TSpec : struct, ISpec, IEquatable<TSpec>
    {
        ValidateKeys(upserts);
        foreach (ref readonly var key in removals) {
            if (ByKey.Remove(key, out var entry) && entry.Cell.IsValid) {
                context.Reconciler.DestroySlot(entry.Cell);
            }
        }
        foreach (ref readonly var item in upserts) {
            UpsertItem(item, Stamp, slotIndex, ref context);
        }
    }

    public void CollectChildren(List<Entity> destination)
    {
        foreach (var entry in ByKey.Values) {
            if (entry.Cell.IsValid) {
                destination.Add(entry.Cell);
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
        var result = Outcome<Exception>.Success;
        try {
            foreach (var key in staleKeys) {
                var cell = ByKey[key].Cell;
                ByKey.Remove(key);
                if (cell.IsValid) {
                    var operation = (Owner: reconciler, Cell: cell);
                    result = result.Attempt(
                        operation,
                        static (in (Reconciler Owner, Entity Cell) value)
                            => value.Owner.DestroySlot(value.Cell));
                }
            }
        }
        finally {
            staleKeys.Clear();
        }
        result.ThrowIfFailed();
    }

    void IRecyclableForEachCleanup.Reset()
    {
        ByKey.Clear();
        _staleKeys.Clear();
        _seenKeys.Clear();
        Stamp = 0;
    }

    private void UpsertItem<TSpec>(
        in Keyed<TKey, TSpec> item,
        int stamp,
        int slotIndex,
        ref GraphContext context)
        where TSpec : struct, ISpec, IEquatable<TSpec>
    {
        if (ByKey.TryGetValue(item.Key, out var entry)
            && entry.Cell is { IsValid: true } cell) {
            if (cell.GetUnchecked<Cell>().Expanded) {
                if (!cell.GetUnchecked<TSpec>().Equals(item.Props)) {
                    cell.GetUnchecked<TSpec>() = item.Props;
                    context.Reconciler.EnqueueDirty(cell);
                }
                entry.Stamp = stamp;
                ByKey[item.Key] = entry;
                return;
            }
            context.Reconciler.DestroySlot(cell);
        }

        var created = context.Reconciler.MountSub(
            item.Props,
            context.Cell,
            context.Depth + 1,
            slotIndex,
            context.Schedule,
            context.Scope,
            context.Output);
        entry.Cell = created;
        entry.Stamp = stamp;
        ByKey[item.Key] = entry;
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
        var index = ctx.Reconciler.RentEachIndex<TKey>();
        ctx.SetSlot(ctx.Reconciler.CreateNode(new EachNode(index)));
        index.Upsert(self.Items.Span, slotIndex, ref ctx);
    }

    public static void Reconcile(
        in ForEachTerm<TKey, TSpec> prev, in ForEachTerm<TKey, TSpec> next,
        ref GraphContext ctx)
    {
        var slot = ctx.PeekSlot();
        if (slot is not { IsValid: true } eachNode) {
            Mount(next, ref ctx);
            return;
        }

        var slotIndex = ctx.NextSlotIndex;
        var index = (EachIndex<TKey>)eachNode.GetUnchecked<EachNode>().Cleanup;
        var stamp = index.Upsert(next.Items.Span, slotIndex, ref ctx);
        index.RemoveStale(ctx.Reconciler, stamp);
        ctx.Advance();
    }
}
