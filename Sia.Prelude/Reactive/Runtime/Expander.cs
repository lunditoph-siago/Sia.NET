namespace Sia.Reactive;

public abstract class Expander
{
    public abstract void Expand(Reconciler reconciler, Entity cell);
}

public sealed class Expander<TSpec, TState, TTree> : Expander
    where TSpec : struct, ISpec<TSpec, TState, TTree>
    where TState : struct
    where TTree : struct, ITerm<TTree>
{
    public static readonly Expander<TSpec, TState, TTree> Instance = new();

    public override void Expand(Reconciler reconciler, Entity cell)
    {
        var props = cell.GetUnchecked<TSpec>();
        var state = cell.GetUnchecked<TState>();
        var cellData = cell.GetUnchecked<Cell>();
        var prevTree = cell.GetUnchecked<PrevTree<TTree>>();

        reconciler.BeginExpansion(cell);
        TTree next;
        try {
            next = TSpec.Expand(props, state, new ExpandContext(reconciler, cell));
            reconciler.CompleteExpansion(cell);
        }
        catch {
            reconciler.AbortExpansion();
            throw;
        }

        cellData = cell.GetUnchecked<Cell>();
        var ctx = new GraphContext(
            reconciler, cell, cellData.Slots, cellData.Depth,
            cellData.Schedule, cellData.Scope);
        ctx.Output = cellData.Output;
        if (prevTree.Mounted) {
            TTree.Reconcile(prevTree.Value, next, ref ctx);
        }
        else {
            TTree.Mount(next, ref ctx);
        }
        cell.GetUnchecked<PrevTree<TTree>>() = new() {
            Value = next,
            Mounted = true,
        };
    }
}
