using System.Buffers;

namespace Sia.Reactors;

public sealed class QuerySubscription : IDisposable
{
    public IReactiveEntityQuery Query { get; }

    private readonly EntityHandler _onAdded;
    private readonly EntityHandler _onRemoved;
    private readonly List<IReactiveEntityHost> _memberHosts = [];
    private ReplayCursor? _activeReplay;
    private HashSet<Entity>? _pendingEntities;
    private bool _disposed;

    private readonly struct HostSnapshot(Entity[]? buffer, int count) : IDisposable
    {
        public bool IsEmpty => count == 0;
        public ReadOnlySpan<Entity> Entities => buffer.AsSpan(0, count);

        public void Dispose()
        {
            if (buffer is not null) {
                ArrayPool<Entity>.Shared.Return(buffer, clearArray: true);
            }
        }
    }

    private sealed class ReplayCursor(HostSnapshot[] snapshots, ReplayCursor? previous)
    {
        public HostSnapshot[] Snapshots { get; } = snapshots;
        public ReplayCursor? Previous { get; } = previous;
        public int SnapshotIndex;
        public int EntityIndex;

        public void AddRemainingEntities(HashSet<Entity> pendingEntities)
        {
            for (var index = SnapshotIndex; index < Snapshots.Length; index++) {
                var entities = Snapshots[index].Entities;
                var startIndex = index == SnapshotIndex ? EntityIndex : 0;
                for (var entityIndex = startIndex; entityIndex < entities.Length; entityIndex++) {
                    pendingEntities.Add(entities[entityIndex]);
                }
            }
        }
    }

    public QuerySubscription(
        IReactiveEntityQuery query, EntityHandler onAdded, EntityHandler onRemoved)
    {
        Query = query;
        _onAdded = onAdded;
        _onRemoved = onRemoved;

        try {
            Query.OnEntityHostAdded += OnHostAdded;
            Query.OnEntityHostRemoved += OnHostRemoved;
            ReplayExistingHosts();
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(Dispose)
                .ThrowFailure();
        }
    }

    private void ReplayExistingHosts()
    {
        var hosts = Query.Hosts.ToArray();
        var snapshots = new HostSnapshot[hosts.Length];
        try {
            for (var i = 0; i < hosts.Length; i++) {
                snapshots[i] = AttachHost(hosts[i]);
            }
            ReplayAdded(snapshots);
        }
        finally {
            foreach (var snapshot in snapshots) {
                snapshot.Dispose();
            }
        }
    }

    private void OnHostAdded(IReactiveEntityHost host)
    {
        using var snapshot = AttachHost(host);
        try {
            if (!snapshot.IsEmpty) {
                ReplayAdded([snapshot]);
            }
        }
        catch (Exception error) {
            if (_memberHosts.Remove(host)) {
                DetachHost(host, Outcome<Exception>.Failure(error)).ThrowFailure();
            }
            throw;
        }
    }

    private HostSnapshot AttachHost(IReactiveEntityHost host)
    {
        if (_disposed || _memberHosts.Contains(host)) {
            return default;
        }

        _memberHosts.Add(host);

        try {
            host.OnEntityCreated += HandleAdded;
            host.OnEntityReleased += HandleRemoved;
            host.OnEntityMovedOut += HandleMovedOut;
            host.OnEntityMovedIn += HandleMovedIn;

            var entities = host.UnsafeGetEntitySpan();
            if (entities.IsEmpty) return default;
            var buffer = ArrayPool<Entity>.Shared.Rent(entities.Length);
            entities.CopyTo(buffer);
            return new(buffer, entities.Length);
        }
        catch (Exception error) {
            _memberHosts.Remove(host);
            return DetachHost(host, Outcome<Exception>.Failure(error)).ThrowFailure<HostSnapshot>();
        }
    }

    private void ReplayAdded(HostSnapshot[] snapshots)
    {
        var cursor = new ReplayCursor(snapshots, _activeReplay);
        _activeReplay = cursor;
        if (_pendingEntities is { } pendingEntities) {
            cursor.AddRemainingEntities(pendingEntities);
        }

        try {
            var hostIndex = -1;
            while (cursor.SnapshotIndex < snapshots.Length && !_disposed) {
                var entities = snapshots[cursor.SnapshotIndex].Entities;
                while (cursor.EntityIndex < entities.Length && !_disposed) {
                    var entity = entities[cursor.EntityIndex++];
                    if (_pendingEntities?.Remove(entity) == false
                        || entity.Host is not { } host) {
                        continue;
                    }
                    if ((uint)hostIndex >= (uint)_memberHosts.Count
                        || !ReferenceEquals(host, _memberHosts[hostIndex])) {
                        hostIndex = host is IReactiveEntityHost reactive
                            ? _memberHosts.IndexOf(reactive)
                            : -1;
                    }
                    if (hostIndex >= 0) {
                        _onAdded(entity);
                    }
                }
                cursor.SnapshotIndex++;
                cursor.EntityIndex = 0;
            }
        }
        catch {
            if (_pendingEntities is { } pending) {
                foreach (var snapshot in snapshots) {
                    foreach (var entity in snapshot.Entities) {
                        pending.Remove(entity);
                    }
                }
            }
            throw;
        }
        finally {
            _activeReplay = cursor.Previous;
            if (_activeReplay is null) {
                _pendingEntities = null;
            }
        }
    }

    private HashSet<Entity>? GetPendingEntities()
    {
        if (_activeReplay is null) {
            return null;
        }
        if (_pendingEntities is null) {
            var pendingEntities = new HashSet<Entity>();
            for (var cursor = _activeReplay; cursor is not null; cursor = cursor.Previous) {
                cursor.AddRemainingEntities(pendingEntities);
            }
            _pendingEntities = pendingEntities;
        }
        return _pendingEntities;
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
                result = result.Attempt((subscription: this, entity),
                    static (in item) => item.subscription.HandleRemoved(item.entity));
            }
        }
        result.ThrowIfFailed();
    }

    private void HandleAdded(Entity entity)
    {
        if (!_disposed) {
            GetPendingEntities()?.Remove(entity);
            _onAdded(entity);
        }
    }

    private void HandleRemoved(Entity entity)
    {
        if (!_disposed && GetPendingEntities()?.Remove(entity) != true) {
            _onRemoved(entity);
        }
    }

    private void HandleMovedOut(Entity entity, IReactiveEntityHost destination)
    {
        // A later observer can still abort the move.
        if (!_disposed && !_memberHosts.Contains(destination)
            && GetPendingEntities()?.Contains(entity) != true) {
            _onRemoved(entity);
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
            .Attempt(this, static (in subscription) => subscription.Query.OnEntityHostAdded -= subscription.OnHostAdded)
            .Attempt(this, static (in subscription) => subscription.Query.OnEntityHostRemoved -= subscription.OnHostRemoved);
        foreach (var host in _memberHosts) {
            result = DetachHost(host, result);
        }
        _memberHosts.Clear();
        _pendingEntities?.Clear();
        _pendingEntities = null;
        result.ThrowIfFailed();
    }

    private Outcome<Exception> DetachHost(
        IReactiveEntityHost host,
        Outcome<Exception> result)
    {
        var state = (subscription: this, host);
        return result
            .Attempt(state, static (in item) => item.host.OnEntityCreated -= item.subscription.HandleAdded)
            .Attempt(state, static (in item) => item.host.OnEntityReleased -= item.subscription.HandleRemoved)
            .Attempt(state, static (in item) => item.host.OnEntityMovedOut -= item.subscription.HandleMovedOut)
            .Attempt(state, static (in item) => item.host.OnEntityMovedIn -= item.subscription.HandleMovedIn);
    }
}
