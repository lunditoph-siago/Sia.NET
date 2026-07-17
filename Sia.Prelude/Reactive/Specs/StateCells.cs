namespace Sia.Reactive;

public sealed class StateCell<T>
    where T : struct
{
    public T Value;
}

public sealed class StateCells
{
    private object[] _cells = new object[4];
    private int _count;
    private int _cursor;
    private bool _initialized;

    internal void BeginExpansion() => _cursor = 0;

    internal void CompleteExpansion()
    {
        if (_cursor != _count) {
            throw HookCountChanged();
        }
        _initialized = true;
    }

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
        if (_initialized) {
            throw HookCountChanged();
        }
        if (_count == _cells.Length) {
            Array.Resize(ref _cells, _cells.Length * 2);
        }
        var cell = new StateCell<T> { Value = initial };
        _cells[_count++] = cell;
        return cell;
    }

    private InvalidOperationException HookCountChanged()
        => new($"Hook count changed from {_count} to {_cursor}; " +
            "hooks must be called unconditionally in the same order.");
}

public readonly struct State<T>(
    StateCell<T> cell,
    Reconciler reconciler,
    Entity owner,
    NodeIdentity identity)
    where T : struct
{
    private readonly StateCell<T> _cell = cell;
    private readonly Reconciler _reconciler = reconciler;
    private readonly EntityReference _owner = new(owner);
    private readonly NodeIdentity _identity = identity;

    public T Value {
        get {
            _ = GetOwner();
            return _cell.Value;
        }
    }

    public void Set(in T value)
    {
        var owner = GetOwner();
        _reconciler.GuardStateMutation(owner);
        _cell.Value = value;
        _reconciler.EnqueueDirty(owner);
    }

    public void Notify()
    {
        var owner = GetOwner();
        _reconciler.GuardStateMutation(owner);
        _reconciler.EnqueueDirty(owner);
    }

    private Entity GetOwner()
        => _reconciler.IsCell(_owner, _identity)
            ? _owner.GetUnchecked()
            : throw new ObjectDisposedException(nameof(State<T>));
}
