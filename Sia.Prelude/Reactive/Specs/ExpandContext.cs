namespace Sia.Reactive;

public readonly ref struct ExpandContext(World world, Entity cell)
{
    public World World { get; } = world;
    public Entity Cell { get; } = cell;

    public StateRef<TState> UseState<TState>()
        where TState : struct
        => new(World, Cell);

    public State<T> UseState<T>(in T initial)
        where T : struct
    {
        ref var cellData = ref Cell.Get<Cell>();
        var states = cellData.States ??= new StateCells();
        return new State<T>(states.NextState(initial), World, Cell);
    }

    public TCtx Use<TCtx>()
        where TCtx : struct
    {
        for (var scope = Cell.Get<Cell>().Scope; scope != null; scope = scope.Parent) {
            if (scope.ContextType != typeof(TCtx)) {
                continue;
            }
            ref var node = ref scope.ProviderSlot.Get<ContextNode<TCtx>>();
            if (!node.Consumers.Contains(Cell)) {
                node.Consumers.Add(Cell);
            }
            return node.Value;
        }
        throw new InvalidOperationException(
            $"No provider found for context type {typeof(TCtx)}.");
    }
}
