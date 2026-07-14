namespace Sia.Reactive;

public readonly struct MountHandle<TProps>
    where TProps : struct
{
    private readonly Reconciler? _owner;
    private readonly Entity? _cell;
    private readonly NodeIdentity _identity;

    internal MountHandle(Reconciler owner, Entity cell, NodeIdentity identity)
        => (_owner, _cell, _identity) = (owner, cell, identity);

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
