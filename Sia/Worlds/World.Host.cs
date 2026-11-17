using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sia;

public partial class World
{
    public event Action<IReactiveEntityHost>? OnEntityHostAdded;
    public event Action<IReactiveEntityHost>? OnEntityHostRemoved;

    public IReadOnlyList<IReactiveEntityHost> Hosts => _hosts.UnsafeRawValues;
    IReadOnlyList<IEntityHost> IEntityQuery.Hosts => _hosts.UnsafeRawValues;

    private readonly SparseSet<IReactiveEntityHost> _hosts = [];
    private readonly HashSet<int> _initializingHosts = [];
    private IReactiveEntityHost? _closingHost;
    private bool _clearingHosts;
    internal int HostCallbackDepth;
    private readonly object _batchObserverLock = new();
    private bool? _hasNoBatchObservers;

    internal void InvalidateBatchObservers()
    {
        lock (_batchObserverLock) _hasNoBatchObservers = null;
    }

    internal void UpdateBatchObservers(ref EntityMigrationHandler? handlers, EntityMigrationHandler? value, bool add)
    {
        lock (_batchObserverLock) {
            var updated = add
                ? (EntityMigrationHandler?)Delegate.Combine(handlers, value)
                : (EntityMigrationHandler?)Delegate.Remove(handlers, value);
            Volatile.Write(ref handlers, updated);
            _hasNoBatchObservers = null;
        }
    }

    internal bool HasNoBatchObservers()
    {
        if (HostCallbackDepth != 0) return false;
        lock (_batchObserverLock) {
            if (_hasNoBatchObservers is { } cached) return cached;
            foreach (var host in Hosts) {
                if (host is not IWorldBatchHost batch || batch.HasMigrationObservers) {
                    _hasNoBatchObservers = false;
                    return false;
                }
            }
            _hasNoBatchObservers = true;
            return true;
        }
    }

    public bool TryGetHost<TEntity, THost>([MaybeNullWhen(false)] out WorldEntityHost<TEntity, THost> host)
        where TEntity : struct, IHList
        where THost : IEntityHost<TEntity>, new()
    {
        ref var rawHost = ref _hosts.GetValueRefOrNullRef(
            WorldEntityHostIndexer<WorldEntityHost<TEntity, THost>>.Index);
        if (Unsafe.IsNullRef(ref rawHost)) {
            host = null;
            return false;
        }
        host = Unsafe.As<WorldEntityHost<TEntity, THost>>(rawHost);
        return true;
    }

    public WorldEntityHost<TEntity, THost> AddHost<TEntity, THost>()
        where TEntity : struct, IHList
        where THost : IEntityHost<TEntity>, new()
        => RegisterHost<WorldEntityHost<TEntity, THost>>(static world => new(world), acquire: false);

    public WorldEntityHost<TEntity, THost> AcquireHost<TEntity, THost>()
        where TEntity : struct, IHList
        where THost : IEntityHost<TEntity>, new()
        => RegisterHost<WorldEntityHost<TEntity, THost>>(static world => new(world), acquire: true);

    internal THost RegisterHost<THost>(Func<World, THost> create, bool acquire)
        where THost : IReactiveEntityHost
    {
        var index = WorldEntityHostIndexer<THost>.Index;
        if (_hosts.TryGetValue(index, out var existing)) {
            if (acquire) return (THost)existing;
            throw new ArgumentException("Host with the same type already exists.");
        }
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_clearingHosts || !_initializingHosts.Add(index)) {
            throw new InvalidOperationException("Cannot register a host during its initialization or while clearing hosts.");
        }
        try {
            var host = create(this);
            _hosts.Add(index, host);
            HostsChanged();
            NotifyObservers(OnEntityHostAdded, host, Outcome<Exception>.Success).ThrowIfFailed();
            return host;
        }
        finally {
            _initializingHosts.Remove(index);
        }
    }

    public THost UnsafeAddRawHost<THost>(THost host)
        where THost : IReactiveEntityHost
    {
        ArgumentNullException.ThrowIfNull(host);
        return RegisterHost(_ => host, acquire: false);
    }

    private void HostsChanged()
    {
        Version++;
        InvalidateBatchObservers();
    }

    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        EnsureOwnerThread();
        var hostIndex = WorldEntityHostIndexer<THost>.Index;
        if (!_hosts.TryGetValue(hostIndex, out var host)) return false;
        if (!CanRemoveHosts) {
            throw new InvalidOperationException("Cannot remove hosts during callbacks, initialization, teardown, or event dispatch.");
        }
        var entities = host.UnsafeGetEntitySpan().ToArray();
        foreach (var entity in entities) {
            if (entity.IsValid && entity.GetStateUnchecked().Releasing) {
                throw new InvalidOperationException("Cannot release a host while an entity is being released.");
            }
        }
        _closingHost = host;
        try {
            var result = Outcome<Exception>.Success;
            foreach (var entity in entities) {
                if (ReferenceEquals(entity.Host, host)) {
                    result = result.Attempt(() => {
                        if (host is IWorldBatchHost owned) owned.ReleaseOwned(entity);
                        else host.Release(entity);
                    });
                }
            }
            if (host.Count != 0) {
                result.Combine(Outcome<Exception>.Failure(
                    new InvalidOperationException("Cannot release a host that still contains entities."))).ThrowIfFailed();
            }
            RemoveHost(hostIndex, host, result).ThrowIfFailed();
            return true;
        }
        finally {
            _closingHost = null;
        }
    }

    public bool TryGetHost<THost>([MaybeNullWhen(false)] out THost host)
        where THost : IEntityHost
    {
        ref var rawHost = ref _hosts.GetValueRefOrNullRef(WorldEntityHostIndexer<THost>.Index);
        if (Unsafe.IsNullRef(ref rawHost)) {
            host = default;
            return false;
        }
        host = (THost)rawHost;
        return true;
    }

    public bool ContainsHost<THost>()
        where THost : IEntityHost
        => _hosts.ContainsKey(WorldEntityHostIndexer<THost>.Index);

    public void ClearHosts()
    {
        EnsureOwnerThread();
        if (!CanRemoveHosts) {
            throw new InvalidOperationException("Cannot remove hosts during callbacks, initialization, teardown, or event dispatch.");
        }
        ClearHostsCore();
    }

    internal bool IsClosingHost(IReactiveEntityHost host)
        => IsDisposed || _clearingHosts || ReferenceEquals(_closingHost, host);

    private bool CanRemoveHosts
        => HostCallbackDepth == 0 && !Dispatcher.IsSending && Dispatcher.PendingAfterSendActionCount == 0
            && !_clearingHosts && _closingHost == null && _initializingHosts.Count == 0;

    private void ClearHostsCore()
    {
        var hosts = _hosts.UnsafeRawValues.ToArray();
        _clearingHosts = true;
        try {
            _hosts.Clear();
            HostsChanged();
            var result = Outcome<Exception>.Success;
            foreach (var host in hosts) result = DisposeDetachedHost(host, result);
            result.ThrowIfFailed();
        }
        finally {
            Count = 0;
            _clearingHosts = false;
        }
    }

    private Outcome<Exception> RemoveHost(int index, IReactiveEntityHost host, Outcome<Exception> result)
    {
        _hosts.Remove(index);
        HostsChanged();
        return DisposeDetachedHost(host, result);
    }

    private Outcome<Exception> DisposeDetachedHost(IReactiveEntityHost host, Outcome<Exception> result)
    {
        result = NotifyObservers(OnEntityHostRemoved, host, result);
        return result.Attempt(host, static (in detached) => detached.Dispose());
    }

    public int ClearEmptyHosts()
    {
        EnsureOwnerThread();
        if (!CanRemoveHosts) {
            throw new InvalidOperationException("Cannot remove hosts during callbacks, initialization, teardown, or event dispatch.");
        }
        int[]? hostsToRemove = null;
        var count = 0;

        foreach (var (key, host) in _hosts) {
            if (host.Count == 0) {
                hostsToRemove ??= ArrayPool<int>.Shared.Rent(_hosts.Count);
                hostsToRemove[count] = key;
                count++;
            }
        }

        if (hostsToRemove != null) {
            var removed = 0;
            var result = Outcome<Exception>.Success;
            try {
                for (var i = 0; i != count; ++i) {
                    var index = hostsToRemove[i];
                    if (!_hosts.TryGetValue(index, out var host) || host.Count != 0) continue;
                    _closingHost = host;
                    result = RemoveHost(index, host, result);
                    removed++;
                }
                result.ThrowIfFailed();
                return removed;
            }
            finally {
                _closingHost = null;
                ArrayPool<int>.Shared.Return(hostsToRemove);
            }
        }

        return 0;
    }
}
