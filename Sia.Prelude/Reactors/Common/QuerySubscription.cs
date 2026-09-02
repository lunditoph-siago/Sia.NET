namespace Sia.Reactors;

public sealed class QuerySubscription : IDisposable
{
    public IReactiveEntityQuery Query { get; }

    private readonly EntityHandler _onAdded;
    private readonly EntityHandler _onRemoved;
    private readonly HashSet<IReactiveEntityHost> _memberHosts = [];
    private bool _disposed;

    public QuerySubscription(
        IReactiveEntityQuery query, EntityHandler onAdded, EntityHandler onRemoved)
    {
        Query = query;
        _onAdded = onAdded;
        _onRemoved = onRemoved;

        Query.OnEntityHostAdded += OnHostAdded;
        foreach (var host in Query.Hosts) {
            OnHostAdded(host);
        }
    }

    private void OnHostAdded(IReactiveEntityHost host)
    {
        _memberHosts.Add(host);

        host.OnEntityCreated += _onAdded;
        host.OnEntityReleased += _onRemoved;
        host.OnEntityMovedOut += HandleMovedOut;
        host.OnEntityMovedIn += HandleMovedIn;

        foreach (var entity in host) {
            _onAdded(entity);
        }
    }

    private void HandleMovedOut(Entity entity, IReactiveEntityHost destination)
    {
        if (!_memberHosts.Contains(destination)) {
            _onRemoved(entity);
        }
    }

    private void HandleMovedIn(Entity entity, IReactiveEntityHost source)
    {
        if (!_memberHosts.Contains(source)) {
            _onAdded(entity);
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;

        Query.OnEntityHostAdded -= OnHostAdded;
        foreach (var host in _memberHosts) {
            host.OnEntityCreated -= _onAdded;
            host.OnEntityReleased -= _onRemoved;
            host.OnEntityMovedOut -= HandleMovedOut;
            host.OnEntityMovedIn -= HandleMovedIn;
        }
        _memberHosts.Clear();
    }
}
