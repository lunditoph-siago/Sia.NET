using System.Buffers;

namespace Sia.Reactors;

public sealed class QuerySubscription : IDisposable
{
    public IReactiveEntityQuery Query { get; }

    private readonly EntityHandler _onAdded;
    private readonly EntityHandler _onRemoved;
    private readonly List<IReactiveEntityHost> _memberHosts = [];
    private ReplayBatch? _replaying;
    private HashSet<Entity>? _pendingReplay;
    private bool _disposed;

    private readonly record struct ReplayGroup(Entity[]? Buffer, int Count)
    {
        public ReadOnlySpan<Entity> Entities => Buffer.AsSpan(0, Count);

        public void Return()
        {
            if (Buffer is not null) ArrayPool<Entity>.Shared.Return(Buffer, clearArray: true);
        }
    }

    private sealed class ReplayBatch(ReplayGroup[] groups, ReplayBatch? previous)
    {
        public ReplayGroup[] Groups { get; } = groups;
        public ReplayBatch? Previous { get; } = previous;
        public int Group;
        public int Index;

        public void AddRemainingTo(HashSet<Entity> pending)
        {
            for (var group = Group; group < Groups.Length; group++) {
                var entities = Groups[group].Entities;
                for (var i = group == Group ? Index : 0; i < entities.Length; i++)
                    pending.Add(entities[i]);
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
            var replay = new ReplayGroup[hosts.Length];
            try {
                for (var i = 0; i < hosts.Length; i++) {
                    replay[i] = AttachHost(hosts[i]);
                }
                ReplayAdded(replay);
            }
            finally {
                foreach (var group in replay) group.Return();
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
            if (entities.Count != 0) ReplayAdded([entities]);
        }
        catch (Exception error) {
            if (_memberHosts.Remove(host)) {
                DetachHost(host, Outcome<Exception>.Failure(error)).ThrowFailure();
            }
            throw;
        }
        finally {
            entities.Return();
        }
    }

    private ReplayGroup AttachHost(IReactiveEntityHost host)
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
            var snapshot = ArrayPool<Entity>.Shared.Rent(entities.Length);
            entities.CopyTo(snapshot);
            return new(snapshot, entities.Length);
        }
        catch (Exception error) {
            _memberHosts.Remove(host);
            return DetachHost(host, Outcome<Exception>.Failure(error)).ThrowFailure<ReplayGroup>();
        }
    }

    private void ReplayAdded(ReplayGroup[] groups)
    {
        var batch = new ReplayBatch(groups, _replaying);
        _replaying = batch;
        if (_pendingReplay is { } existing) batch.AddRemainingTo(existing);

        try {
            while (batch.Group < groups.Length && !_disposed) {
                var entities = groups[batch.Group].Entities;
                while (batch.Index < entities.Length && !_disposed) {
                    var entity = entities[batch.Index++];
                    if ((_pendingReplay?.Remove(entity) ?? true)
                        && entity.Host is IReactiveEntityHost host
                        && _memberHosts.Contains(host)) {
                        _onAdded(entity);
                    }
                }
                batch.Group++;
                batch.Index = 0;
            }
        }
        catch {
            if (_pendingReplay is { } pending) {
                foreach (var group in groups)
                    foreach (var entity in group.Entities)
                        pending.Remove(entity);
            }
            throw;
        }
        finally {
            _replaying = batch.Previous;
            if (_replaying is null) _pendingReplay = null;
        }
    }

    private bool ForgetPending(Entity entity)
    {
        if (_replaying is null) return false;
        if (_pendingReplay is null) {
            // Callbacks usually leave membership alone. Build the lookup only
            // when a nested notification needs to cancel a pending replay.
            var pending = new HashSet<Entity>();
            for (var batch = _replaying; batch is not null; batch = batch.Previous)
                batch.AddRemainingTo(pending);
            _pendingReplay = pending;
        }
        return _pendingReplay.Remove(entity);
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
            ForgetPending(entity);
            _onAdded(entity);
        }
    }

    private void HandleRemoved(Entity entity)
    {
        if (!_disposed && !ForgetPending(entity)) {
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
