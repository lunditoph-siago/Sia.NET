namespace Sia.Reactive;

public ref struct Hooks
{
    private readonly Reconciler _reconciler;
    private readonly Entity _cell;
    private StateCells? _states;

    internal Hooks(in ExpandContext context)
    {
        _reconciler = context.Reconciler;
        _cell = context.Cell;
        _states = null;
    }

    public State<T> UseState<T>(in T initial)
        where T : struct
    {
        ref var cellData = ref _cell.GetUnchecked<Cell>();
        return new State<T>(
            EnsureStateCells().NextState(initial),
            _reconciler,
            _cell,
            cellData.Identity);
    }

    public void UseEffect<TDependencies, TResource>(
        scoped in TDependencies dependencies,
        [NestedCallback] ReactiveEffectSetup<TDependencies, TResource> setup,
        [NestedCallback] ReactiveEffectCleanup<TResource> cleanup)
        where TDependencies : struct, IEquatable<TDependencies>
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(cleanup);
        EnsureStateCells().NextEffect(
            _reconciler,
            dependencies,
            setup,
            cleanup);
    }

    private StateCells EnsureStateCells()
        => _states ??= _reconciler.EnsureStateCells(_cell);

    internal void CompleteRender()
        => _cell.GetUnchecked<Cell>().HookLayoutInitialized = true;
}
