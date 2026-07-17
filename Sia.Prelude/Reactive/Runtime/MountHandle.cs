namespace Sia.Reactive;

public readonly struct MountHandle<TProps>(
    Reconciler owner,
    Entity cell,
    NodeIdentity identity)
    where TProps : struct
{
    private readonly Reconciler? _owner = owner;
    private readonly EntityReference _cell = new(cell);
    private readonly NodeIdentity _identity = identity;

    public bool IsMounted
        => _owner != null
            && _owner.IsCell(_cell, _identity);

    public TProps Props => GetCell().GetUnchecked<TProps>();

    public void Update(in TProps props)
        => GetOwner().UpdateMount(GetCell(), props);

    public void Invalidate()
        => GetOwner().InvalidateMount(GetCell());

    public void Unmount()
        => GetOwner().Unmount(GetCell(), _identity);

    private Entity GetCell()
        => _owner != null
            && _owner.IsCell(_cell, _identity)
                ? _cell.GetUnchecked()
                : throw new ObjectDisposedException(nameof(MountHandle<TProps>));

    private Reconciler GetOwner()
        => _owner ?? throw new ObjectDisposedException(nameof(MountHandle<TProps>));
}
