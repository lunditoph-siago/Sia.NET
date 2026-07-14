namespace Sia.Reactive;

public readonly struct StateRef<TState>
    where TState : struct
{
    private readonly Reconciler? _reconciler;
    private readonly Entity? _owner;
    private readonly NodeIdentity _identity;

    internal StateRef(Reconciler reconciler, Entity owner, NodeIdentity identity)
        => (_reconciler, _owner, _identity) = (reconciler, owner, identity);

    public TState Value => GetOwner().Get<TState>();

    public void Set(in TState value)
    {
        var owner = GetOwner();
        var reconciler = GetReconciler();
        reconciler.GuardStateMutation(owner);
        owner.Get<TState>() = value;
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
            && _owner != null
            && _reconciler.IsCell(_owner, _identity)
                ? _owner
                : throw new ObjectDisposedException(nameof(StateRef<TState>));

    private Reconciler GetReconciler()
        => _reconciler ?? throw new ObjectDisposedException(nameof(StateRef<TState>));
}
