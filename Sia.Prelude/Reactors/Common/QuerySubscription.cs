namespace Sia.Reactors;

public sealed class QuerySubscription : IDisposable
{
    public IReactiveEntityQuery Query { get; }

    private readonly EntityHandler _onAdded;
    private readonly EntityHandler _onRemoved;
    private readonly List<IReactiveEntityHost> _memberHosts = [];
    private bool _disposed;

    public QuerySubscription(
        IReactiveEntityQuery query, EntityHandler onAdded, EntityHandler onRemoved)
    {
        Query = query;
        _onAdded = onAdded;
        _onRemoved = onRemoved;

        try {
            Query.OnEntityHostAdded += OnHostAdded;
            Query.OnEntityHostRemoved += OnHostRemoved;
            foreach (var host in Query.Hosts) {
                OnHostAdded(host);
            }
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(Dispose)
                .ThrowFailure();
        }
    }

    private void OnHostAdded(IReactiveEntityHost host)
    {
        if (_memberHosts.Contains(host)) {
            return;
        }
        _memberHosts.Add(host);

        try {
            host.OnEntityCreated += _onAdded;
            host.OnEntityReleased += _onRemoved;
            host.OnEntityMovedOut += HandleMovedOut;
            host.OnEntityMovedIn += HandleMovedIn;

            foreach (var entity in host) {
                _onAdded(entity);
            }
        }
        catch (Exception error) {
            _memberHosts.Remove(host);
            DetachHost(host, Outcome<Exception>.Failure(error)).ThrowFailure();
        }
    }

    private void OnHostRemoved(IReactiveEntityHost host)
    {
        if (!_memberHosts.Contains(host)) {
            return;
        }

        var entities = host.UnsafeGetEntitySpan().ToArray();
        _memberHosts.Remove(host);
        var result = DetachHost(host, Outcome<Exception>.Success);
        foreach (var entity in entities) {
            if (entity.IsValid) {
                result = result.Attempt(() => _onRemoved(entity));
            }
        }
        result.ThrowIfFailed();
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

        var result = Outcome<Exception>.Success
            .Attempt(() => Query.OnEntityHostAdded -= OnHostAdded)
            .Attempt(() => Query.OnEntityHostRemoved -= OnHostRemoved);
        foreach (var host in _memberHosts) {
            result = DetachHost(host, result);
        }
        _memberHosts.Clear();
        result.ThrowIfFailed();
    }

    private Outcome<Exception> DetachHost(
        IReactiveEntityHost host,
        Outcome<Exception> result)
        => result
            .Attempt(() => host.OnEntityCreated -= _onAdded)
            .Attempt(() => host.OnEntityReleased -= _onRemoved)
            .Attempt(() => host.OnEntityMovedOut -= HandleMovedOut)
            .Attempt(() => host.OnEntityMovedIn -= HandleMovedIn);
}
