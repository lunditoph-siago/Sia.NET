namespace Sia.Reactive;

public readonly struct MountHandle<TProps>(
    Reconciler owner,
    Entity cell,
    NodeIdentity identity)
    where TProps : struct
{
    private readonly Reconciler? _owner = owner;
    private readonly Entity? _cell = cell;
    private readonly NodeIdentity _identity = identity;

    public bool IsMounted
        => _owner != null
            && _cell != null
            && _owner.IsCell(_cell, _identity);

    public TProps Props => GetCell().Get<TProps>();

    public void Update(in TProps props)
        => GetOwner().UpdateMount(GetCell(), props);

    public void Invalidate()
        => GetOwner().InvalidateMount(GetCell());

    public void Unmount()
        => GetOwner().Unmount(GetCell(), _identity);

    private Entity GetCell()
        => IsMounted
            ? _cell!
            : throw new ObjectDisposedException(nameof(MountHandle<TProps>));

    private Reconciler GetOwner()
        => _owner ?? throw new ObjectDisposedException(nameof(MountHandle<TProps>));
}
