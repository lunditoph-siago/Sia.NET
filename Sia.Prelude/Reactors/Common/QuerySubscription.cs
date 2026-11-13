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

    private readonly record struct HostSnapshot(Entity[]? Buffer, int Count)
    {
        public ReadOnlySpan<Entity> Entities => Buffer.AsSpan(0, Count);

        public void Return()
        {
            if (Buffer is not null) {
                ArrayPool<Entity>.Shared.Return(Buffer, clearArray: true);
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
            var hosts = Query.Hosts.ToArray();
            var snapshots = new HostSnapshot[hosts.Length];
            try {
                for (var i = 0; i < hosts.Length; i++) {
                    snapshots[i] = AttachHost(hosts[i]);
                }

                // Capture every host before notifying callbacks that may move entities.
                ReplayAdded(snapshots);
            }
            finally {
                foreach (var snapshot in snapshots) {
                    snapshot.Return();
                }
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
        var snapshot = AttachHost(host);
        try {
            if (snapshot.Count != 0) {
                ReplayAdded([snapshot]);
            }
        }
        catch (Exception error) {
            if (_memberHosts.Remove(host)) {
                DetachHost(host, Outcome<Exception>.Failure(error)).ThrowFailure();
            }
            throw;
        }
        finally {
            snapshot.Return();
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
            if (entities.IsEmpty) {
                return default;
            }
            var snapshot = ArrayPool<Entity>.Shared.Rent(entities.Length);
            entities.CopyTo(snapshot);
            return new(snapshot, entities.Length);
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
            while (cursor.SnapshotIndex < snapshots.Length && !_disposed) {
                var entities = snapshots[cursor.SnapshotIndex].Entities;
                while (cursor.EntityIndex < entities.Length && !_disposed) {
                    // Advance before the callback so nested notifications see only unread entities.
                    var entity = entities[cursor.EntityIndex++];
                    if ((_pendingEntities?.Remove(entity) ?? true)
                        && entity.Host is IReactiveEntityHost host
                        && _memberHosts.Contains(host)) {
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

    private bool CancelPendingReplay(Entity entity)
    {
        if (_activeReplay is null) {
            return false;
        }
        if (_pendingEntities is null) {
            // Build the lookup only when a nested notification needs it.
            var pendingEntities = new HashSet<Entity>();
            for (var cursor = _activeReplay; cursor is not null; cursor = cursor.Previous) {
                cursor.AddRemainingEntities(pendingEntities);
            }
            _pendingEntities = pendingEntities;
        }
        return _pendingEntities.Remove(entity);
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
            CancelPendingReplay(entity);
            _onAdded(entity);
        }
    }

    private void HandleRemoved(Entity entity)
    {
        if (!_disposed && !CancelPendingReplay(entity)) {
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
        _pendingEntities?.Clear();
        _pendingEntities = null;
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
