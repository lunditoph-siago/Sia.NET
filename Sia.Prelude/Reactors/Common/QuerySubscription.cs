namespace Sia.Reactors;

public sealed class QuerySubscription : IDisposable
{
    public IReactiveEntityQuery Query { get; }

    private readonly EntityHandler _onAdded;
    private readonly EntityHandler _onRemoved;
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
        host.OnEntityCreated += _onAdded;
        host.OnEntityReleased += _onRemoved;

        foreach (var entity in host) {
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
        foreach (var host in Query.Hosts) {
            host.OnEntityCreated -= _onAdded;
            host.OnEntityReleased -= _onRemoved;
        }
    }
}
