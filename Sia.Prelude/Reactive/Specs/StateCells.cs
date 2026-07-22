namespace Sia.Reactive;

using System.Runtime.CompilerServices;

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

    public StateCells() {}

    internal StateCells(bool initialized) => _initialized = initialized;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void BeginExpansion() => _cursor = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CompleteExpansion()
    {
        if (_cursor != _count) {
            throw HookCountChanged();
        }
        _initialized = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StateCell<T> NextState<T>(in T initial)
        where T : struct
    {
        var index = _cursor++;
        if (index < _count) {
            return _cells[index] as StateCell<T>
                ?? throw HookTypeChanged(index);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void NextEffect<TDependencies, TResource>(
        Reconciler reconciler,
        in TDependencies dependencies,
        ReactiveEffectSetup<TDependencies, TResource> setup,
        ReactiveEffectCleanup<TResource> cleanup)
        where TDependencies : struct, IEquatable<TDependencies>
    {
        var index = _cursor++;
        if (index < _count) {
            if (_cells[index] is not EffectLifetime<
                    TDependencies, TResource> effect) {
                throw HookTypeChanged(index);
            }
            effect.Reconcile(dependencies, setup, cleanup);
            return;
        }
        if (_initialized) {
            throw HookCountChanged();
        }
        if (_count == _cells.Length) {
            Array.Resize(ref _cells, _cells.Length * 2);
        }
        var created = new EffectLifetime<TDependencies, TResource>(
            reconciler,
            dependencies,
            setup,
            cleanup);
        _cells[_count++] = created;
        created.ScheduleSetup();
    }

    internal void Unmount()
    {
        var result = Outcome<Exception>.Success;
        for (var index = _count - 1; index >= 0; index--) {
            if (_cells[index] is IEffectCleanup cleanup) {
                result = result.Attempt(cleanup.Unmount);
            }
            _cells[index] = null!;
        }
        _count = 0;
        _cursor = 0;
        _initialized = false;
        result.ThrowIfFailed();
    }

    private InvalidOperationException HookCountChanged()
        => new($"Hook count changed from {_count} to {_cursor}; " +
            "hooks must be called unconditionally in the same order.");

    private static InvalidOperationException HookTypeChanged(int index)
        => new($"Hook #{index} was previously a different type; " +
            "hooks must be called in the same order on every expansion.");
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            _ = GetOwner();
            return _cell.Value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(in T value)
    {
        var owner = GetOwner();
        _reconciler.GuardStateMutation(owner);
        if (EqualityComparer<T>.Default.Equals(_cell.Value, value)) {
            return;
        }
        _cell.Value = value;
        _reconciler.EnqueueDirty(owner);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Notify()
    {
        var owner = GetOwner();
        _reconciler.GuardStateMutation(owner);
        _reconciler.EnqueueDirty(owner);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Entity GetOwner()
        => _reconciler.IsCell(_owner, _identity)
            ? _owner.GetUnchecked()
            : throw new ObjectDisposedException(nameof(State<T>));
}
