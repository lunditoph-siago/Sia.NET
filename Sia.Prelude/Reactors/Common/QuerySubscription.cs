namespace Sia.Reactors;

public sealed class QuerySubscription : IDisposable
{
    public IReactiveEntityQuery Query { get; }

    private readonly EntityHandler _onAdded;
    private readonly EntityHandler _onRemoved;
    private readonly List<IReactiveEntityHost> _memberHosts = [];
    private HashSet<Entity>? _pendingReplay;
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
            var hosts = Query.Hosts.ToArray();
            var replay = new Entity[hosts.Length][];
            for (var i = 0; i < hosts.Length; i++) {
                replay[i] = AttachHost(hosts[i]);
            }
            foreach (var entities in replay) {
                ReplayAdded(entities);
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
        var entities = AttachHost(host);
        try {
            ReplayAdded(entities);
        }
        catch (Exception error) {
            if (_memberHosts.Remove(host)) {
                DetachHost(host, Outcome<Exception>.Failure(error)).ThrowFailure();
            }
            throw;
        }
    }

    private Entity[] AttachHost(IReactiveEntityHost host)
    {
        if (_disposed || _memberHosts.Contains(host)) {
            return [];
        }

        _memberHosts.Add(host);

        try {
            host.OnEntityCreated += HandleAdded;
            host.OnEntityReleased += HandleRemoved;
            host.OnEntityMovedOut += HandleMovedOut;
            host.OnEntityMovedIn += HandleMovedIn;

            var entities = host.UnsafeGetEntitySpan().ToArray();
            if (entities.Length != 0) {
                (_pendingReplay ??= []).UnionWith(entities);
            }
            return entities;
        }
        catch (Exception error) {
            _memberHosts.Remove(host);
            return DetachHost(host, Outcome<Exception>.Failure(error)).ThrowFailure<Entity[]>();
        }
    }

    private void ReplayAdded(Entity[] entities)
    {
        if (entities.Length == 0 || _pendingReplay is not { } pending) {
            return;
        }

        try {
            foreach (var entity in entities) {
                if (pending.Remove(entity)
                    && entity.Host is IReactiveEntityHost host
                    && _memberHosts.Contains(host)) {
                    _onAdded(entity);
                }
            }
        }
        catch {
            pending.ExceptWith(entities);
            throw;
        }
        finally {
            if (pending.Count == 0 && ReferenceEquals(_pendingReplay, pending)) {
                _pendingReplay = null;
            }
        }
    }

    private void OnHostRemoved(IReactiveEntityHost host)
    {
        if (_disposed || !_memberHosts.Remove(host)) {
            return;
        }

        var result = DetachHost(host, Outcome<Exception>.Success);
        Entity[] entities = [];
        result = result.Attempt(() => entities = host.UnsafeGetEntitySpan().ToArray());
        foreach (var entity in entities) {
            if (ReferenceEquals(entity.Host, host)) {
                result = result.Attempt(entity, (in Entity value) => HandleRemoved(value));
            }
        }
        result.ThrowIfFailed();
    }

    private void HandleAdded(Entity entity)
    {
        if (!_disposed) {
            _pendingReplay?.Remove(entity);
            _onAdded(entity);
        }
    }

    private void HandleRemoved(Entity entity)
    {
        if (!_disposed && _pendingReplay?.Remove(entity) != true) {
            _onRemoved(entity);
        }
    }

    private void HandleMovedOut(Entity entity, IReactiveEntityHost destination)
    {
        if (!_memberHosts.Contains(destination)) {
            HandleRemoved(entity);
        }
    }

    private void HandleMovedIn(Entity entity, IReactiveEntityHost source)
    {
        if (!_memberHosts.Contains(source)) {
            HandleAdded(entity);
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
        _pendingReplay?.Clear();
        _pendingReplay = null;
        result.ThrowIfFailed();
    }

    private Outcome<Exception> DetachHost(
        IReactiveEntityHost host,
        Outcome<Exception> result)
        => result
            .Attempt(() => host.OnEntityCreated -= HandleAdded)
            .Attempt(() => host.OnEntityReleased -= HandleRemoved)
            .Attempt(() => host.OnEntityMovedOut -= HandleMovedOut)
            .Attempt(() => host.OnEntityMovedIn -= HandleMovedIn);
}
