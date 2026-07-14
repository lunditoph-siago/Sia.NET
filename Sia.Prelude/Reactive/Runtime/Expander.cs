namespace Sia.Reactive;

public abstract class Expander
{
    public abstract void Expand(Reconciler reconciler, Entity cell);
}

internal sealed class Expander<TSpec, TState, TTree> : Expander
    where TSpec : struct, ISpec<TSpec, TState, TTree>
    where TState : struct
    where TTree : struct, ITerm<TTree>
{
    public static readonly Expander<TSpec, TState, TTree> Instance = new();

    private Expander() {}

    public override void Expand(Reconciler reconciler, Entity cell)
    {
        var world = reconciler.World;
        var props = cell.Get<TSpec>();
        var state = cell.Get<TState>();
        var cellData = cell.Get<Cell>();
        var prevTree = cell.Get<PrevTree<TTree>>();

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

        var ctx = new GraphContext(
            reconciler, world, cell, cellData.Slots, cellData.Depth,
            cellData.Schedule, cellData.Scope);
        if (prevTree.Mounted) {
            TTree.Reconcile(prevTree.Value, next, ref ctx);
        }
        else {
            TTree.Mount(next, ref ctx);
        }
        cell.Get<PrevTree<TTree>>() = new() { Value = next, Mounted = true };
    }
}
