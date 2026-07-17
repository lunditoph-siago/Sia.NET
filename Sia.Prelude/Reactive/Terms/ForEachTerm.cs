namespace Sia.Reactive;

public readonly record struct Keyed<TKey, TSpec>(TKey Key, TSpec Props)
    where TKey : notnull
    where TSpec : struct, ISpec, IEquatable<TSpec>;

public interface IForEachCleanup
{
    void DestroyChildren(Reconciler reconciler);
}

internal interface IRecyclableForEachCleanup : IForEachCleanup
{
    void Reset();
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

    public void DestroyChildren(Reconciler reconciler)
    {
        var result = Outcome<Exception>.Success;
        foreach (var entry in ByKey.Values) {
            var cell = entry.Cell;
            if (!cell.IsValid) {
                continue;
            }
            var operation = (Owner: reconciler, Cell: cell);
            result = result.Attempt(
                operation,
                static (in (Reconciler Owner, Entity Cell) value)
                    => value.Owner.DestroySlot(value.Cell));
        }
        ByKey.Clear();
        result.ThrowIfFailed();
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
        Upsert(index, self.Items.Span, slotIndex, ref ctx);
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
        var stamp = Upsert(index, next.Items.Span, slotIndex, ref ctx);
        index.RemoveStale(ctx.Reconciler, stamp);
        ctx.Advance();
    }

    private static int Upsert(
        EachIndex<TKey> index, ReadOnlySpan<Keyed<TKey, TSpec>> items, int slotIndex,
        ref GraphContext ctx)
    {
        index.ValidateKeys(items);
        var stamp = ++index.Stamp;
        var byKey = index.ByKey;
        foreach (ref readonly var item in items) {
            if (byKey.TryGetValue(item.Key, out var entry)
                && entry.Cell is { IsValid: true } cell) {
                if (!cell.GetUnchecked<TSpec>().Equals(item.Props)) {
                    cell.GetUnchecked<TSpec>() = item.Props;
                    ctx.Reconciler.EnqueueDirty(cell);
                }
            }
            else {
                var created = ctx.Reconciler.MountSub(
                    item.Props, ctx.Cell, ctx.Depth + 1, slotIndex, ctx.Schedule, ctx.Scope);
                entry.Cell = created;
            }
            entry.Stamp = stamp;
            byKey[item.Key] = entry;
        }
        return stamp;
    }
}
