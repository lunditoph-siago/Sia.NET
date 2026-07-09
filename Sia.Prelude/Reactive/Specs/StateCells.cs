namespace Sia.Reactive;

internal sealed class StateCell<T>
    where T : struct
{
    public T Value;
}

public sealed class StateCells
{
    private object[] _cells = new object[4];
    private int _count;
    private int _cursor;

    internal void ResetCursor() => _cursor = 0;

    internal StateCell<T> NextState<T>(in T initial)
        where T : struct
    {
        var index = _cursor++;
        if (index < _count) {
            return _cells[index] as StateCell<T>
                ?? throw new InvalidOperationException(
                    $"Hook #{index} was previously a different type; " +
                    "hooks must be called in the same order on every expansion.");
        }
        if (_count == _cells.Length) {
            Array.Resize(ref _cells, _cells.Length * 2);
        }
        var cell = new StateCell<T> { Value = initial };
        _cells[_count++] = cell;
        return cell;
    }
}

public readonly struct State<T>
    where T : struct
{
    private readonly StateCell<T> _cell;
    private readonly World _world;
    private readonly Entity _owner;

    internal State(StateCell<T> cell, World world, Entity owner)
    {
        _cell = cell;
        _world = world;
        _owner = owner;
    }

    public T Value => _cell.Value;

    public ref T Ref => ref _cell.Value;

    public void Set(in T value)
    {
        _cell.Value = value;
        Notify();
    }

    public void Notify()
        => _world.Send(_owner, CellEvents.Invalidate.Instance);
}
