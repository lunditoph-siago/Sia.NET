namespace Sia.Reactive;

public readonly struct StateRef<TState>(
    Reconciler reconciler,
    Entity owner,
    NodeIdentity identity)
    where TState : struct
{
    private readonly Reconciler? _reconciler = reconciler;
    private readonly EntityReference _owner = new(owner);
    private readonly NodeIdentity _identity = identity;

    public TState Value => GetOwner().GetUnchecked<TState>();

    public void Set(in TState value)
    {
        var owner = GetOwner();
        var reconciler = GetReconciler();
        reconciler.GuardStateMutation(owner);
        ref var current = ref owner.GetUnchecked<TState>();
        if (EqualityComparer<TState>.Default.Equals(current, value)) {
            return;
        }
        current = value;
        reconciler.EnqueueDirty(owner);
    }

    public void Notify()
    {
        var owner = GetOwner();
        var reconciler = GetReconciler();
        reconciler.GuardStateMutation(owner);
        reconciler.EnqueueDirty(owner);
    }

    private Entity GetOwner()
        => _reconciler != null
            && _reconciler.IsCell(_owner, _identity)
                ? _owner.GetUnchecked()
                : throw new ObjectDisposedException(nameof(StateRef<TState>));

    private Reconciler GetReconciler()
        => _reconciler ?? throw new ObjectDisposedException(nameof(StateRef<TState>));
}
