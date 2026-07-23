namespace Sia.Reactive;

internal struct DeferredLifecycle
{
    private long _version;

    public bool Mounted { get; private set; }
    public bool Disposed { get; private set; }

    public long NextVersion() => ++_version;

    public readonly bool IsCurrent(long version) => !Disposed && version == _version;

    public void MarkMounted() => Mounted = true;

    public bool TryBeginCleanup()
    {
        if (!Mounted) {
            return false;
        }
        Mounted = false;
        return true;
    }

    public bool TryBeginUnmount()
    {
        if (Disposed) {
            return false;
        }
        Disposed = true;
        _version++;
        return true;
    }
}
