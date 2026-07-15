namespace Sia.Reactive;

public readonly ref struct ExpandContext(Reconciler reconciler, Entity cell)
{
    public Reconciler Reconciler { get; } = reconciler;
    public World World => Reconciler.World;
    public Entity Cell { get; } = cell;

    public StateRef<TState> UseState<TState>()
        where TState : struct
        => new(Reconciler, Cell, Cell.Get<Cell>().Identity);

    public State<T> UseState<T>(in T initial)
        where T : struct
    {
        ref var cellData = ref Cell.Get<Cell>();
        var states = cellData.States ??= new StateCells();
        return new State<T>(
            states.NextState(initial), Reconciler, Cell, cellData.Identity);
    }

    public TCtx Use<TCtx>()
        where TCtx : struct
    {
        for (var scope = Cell.Get<Cell>().Scope; scope != null; scope = scope.Parent) {
            if (scope.ContextType != typeof(TCtx)) {
                continue;
            }
            ref var node = ref scope.ProviderSlot.Get<ContextNode<TCtx>>();
            Reconciler.RecordContextDependency(Cell, scope);
            return node.Value;
        }
        throw new InvalidOperationException(
            $"No provider found for context type {typeof(TCtx)}.");
    }
}
