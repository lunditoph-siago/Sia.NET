namespace Sia.Reactive;

public readonly ref struct ExpandContext(Reconciler reconciler, Entity cell)
{
    public Reconciler Reconciler { get; } = reconciler;
    public World World => Reconciler.World;
    public Entity Cell { get; } = cell;

    public StateRef<TState> UseState<TState>()
        where TState : struct
        => new(Reconciler, Cell, Cell.GetUnchecked<Cell>().Identity);

    public State<T> UseState<T>(in T initial)
        where T : struct
    {
        ref var cellData = ref Cell.GetUnchecked<Cell>();
        var states = cellData.States ??= new StateCells(cellData.Expanded);
        return new State<T>(
            states.NextState(initial), Reconciler, Cell, cellData.Identity);
    }

    public TCtx Use<TCtx>()
        where TCtx : struct
        => ContextLookup.Get<TCtx>(Reconciler, Cell);
}
