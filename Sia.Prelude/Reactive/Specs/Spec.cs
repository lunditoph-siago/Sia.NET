namespace Sia.Reactive;

public delegate TTree ExpandFn<TProps, TTree>(in TProps props, in ExpandContext ctx)
    where TProps : struct, IEquatable<TProps>
    where TTree : struct, ITerm<TTree>;

public abstract class Spec<TProps>
    where TProps : struct, IEquatable<TProps>
{
    internal abstract Entity MountCell(
        Reconciler reconciler, in TProps props, Entity? parent, int depth,
        int slotInParent, ScheduleRegistry? schedule, ContextScope? scope);
}

public static class Spec
{
    public static Spec<TProps> Of<TProps, TTree>(ExpandFn<TProps, TTree> expand)
        where TProps : struct, IEquatable<TProps>
        where TTree : struct, ITerm<TTree>
        => new FnSpec<TProps, TTree>(expand);
}

internal sealed class FnSpec<TProps, TTree>(ExpandFn<TProps, TTree> expand)
    : Spec<TProps>
    where TProps : struct, IEquatable<TProps>
    where TTree : struct, ITerm<TTree>
{
    private readonly FnExpander<TProps, TTree> _expander = new(expand);

    internal override Entity MountCell(
        Reconciler reconciler, in TProps props, Entity? parent, int depth,
        int slotInParent, ScheduleRegistry? schedule, ContextScope? scope)
    {
        var cell = reconciler.GraphWorld.Create(HList.From(
            props,
            new PrevTree<TTree>(),
            new Cell {
                Identity = reconciler.NextIdentity(),
                Parent = parent,
                Depth = depth,
                SlotInParent = slotInParent,
                Slots = TTree.SlotCount > 0 ? new CellSlot[TTree.SlotCount] : [],
                Expander = _expander,
                Schedule = schedule,
                Scope = scope,
            }));
        _expander.Expand(reconciler, cell);
        return cell;
    }
}

internal sealed class FnExpander<TProps, TTree>(ExpandFn<TProps, TTree> expand)
    : Expander
    where TProps : struct, IEquatable<TProps>
    where TTree : struct, ITerm<TTree>
{
    public override void Expand(Reconciler reconciler, Entity cell)
    {
        var world = reconciler.World;
        var props = cell.Get<TProps>();
        var cellData = cell.Get<Cell>();
        var prevTree = cell.Get<PrevTree<TTree>>();

        cellData.States?.ResetCursor();
        var next = expand(props, new ExpandContext(world, cell));

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
