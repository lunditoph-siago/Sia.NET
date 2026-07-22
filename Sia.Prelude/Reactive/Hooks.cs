namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public ref struct Hooks
{
    private readonly Reconciler _reconciler;
    private readonly Entity _cell;
    private StateCells? _states;

    internal Hooks(in ExpandContext context)
    {
        _reconciler = context.Reconciler;
        _cell = context.Cell;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public State<T> UseState<T>(in T initial)
        where T : struct
    {
        ref var cellData = ref _cell.GetUnchecked<Cell>();
        var states = _states ??= cellData.States ??=
            new StateCells(_cell.GetUnchecked<PrevTree<OpaqueTerm>>().Mounted);
        return new State<T>(states.NextState(initial), _reconciler, _cell, cellData.Identity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UseEffect<TDependencies, TResource>(
        scoped in TDependencies dependencies,
        [NestedCallback] ReactiveEffectSetup<TDependencies, TResource> setup,
        [NestedCallback] ReactiveEffectCleanup<TResource> cleanup)
        where TDependencies : struct, IEquatable<TDependencies>
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(cleanup);
        ref var cellData = ref _cell.GetUnchecked<Cell>();
        var states = _states ??= cellData.States ??=
            new StateCells(_cell.GetUnchecked<PrevTree<OpaqueTerm>>().Mounted);
        states.NextEffect(_reconciler, dependencies, setup, cleanup);
    }
}
