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

    public WorldEntityHost<TEntity, THost> AcquireHost<TEntity, THost>()
        where TEntity : struct, IHList
        where THost : IEntityHost<TEntity>, new()
        => TryGetHost<TEntity, THost>(out var host)
            ? host : AddHost<WorldEntityHost<TEntity, THost>>(static world => new(world));

    public THost AddHost<THost>(Func<World, THost> create)
        where THost : IReactiveEntityHost
    {
        ArgumentNullException.ThrowIfNull(create);
        var index = WorldEntityHostIndexer<THost>.Index;
        if (_hosts.ContainsKey(index)) {
            throw new ArgumentException("Host with the same type already exists.");
        }
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_clearingHosts || !_initializingHosts.Add(index)) {
            throw new InvalidOperationException("Cannot register a host during its initialization or while clearing hosts.");
        }
        try {
            var host = create(this);
            IReactiveEntityHost registered = host;
            ArgumentNullException.ThrowIfNull(registered, nameof(host));
            _hosts.Add(index, registered);
            Version++;
            InvalidateBatchObservers();
            NotifyObservers(OnEntityHostAdded, registered, Outcome<Exception>.Success).ThrowIfFailed();
            return host;
        }
        finally {
            _initializingHosts.Remove(index);
        }
    }

    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        EnsureOwnerThread();
        var hostIndex = WorldEntityHostIndexer<THost>.Index;
        if (!_hosts.TryGetValue(hostIndex, out var host)) return false;
        if (HostCallbackDepth != 0 || Dispatcher.IsSending || Dispatcher.PendingAfterSendActionCount != 0
            || _clearingHosts || _closingHost != null || _initializingHosts.Count != 0) {
            throw new InvalidOperationException("Cannot remove hosts during callbacks, initialization, teardown, or event dispatch.");
        }
        var entities = host.UnsafeGetEntitySpan().ToArray();
        _closingHost = host;
        try {
            var result = Outcome<Exception>.Success;
            foreach (var entity in entities) {
                if (ReferenceEquals(entity.Host, host)) {
                    result = result.Attempt((host, entity), static (in item) => item.host.Release(item.entity));
                }
            }
            if (host.Count != 0) {
                result.Combine(Outcome<Exception>.Failure(
                    new InvalidOperationException("Cannot release a host that still contains entities."))).ThrowIfFailed();
            }
            _hosts.Remove(hostIndex);
            Version++;
            InvalidateBatchObservers();
            result = NotifyObservers(OnEntityHostRemoved, host, result);
            result.Attempt(host, static (in detached) => detached.Dispose()).ThrowIfFailed();
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
        if (!IsDisposed) {
            EnsureOwnerThread();
        }
        if (HostCallbackDepth != 0 || Dispatcher.IsSending || Dispatcher.PendingAfterSendActionCount != 0
            || _clearingHosts || _closingHost != null || _initializingHosts.Count != 0) {
            throw new InvalidOperationException("Cannot remove hosts during callbacks, initialization, teardown, or event dispatch.");
        }

        var hosts = _hosts.UnsafeRawValues.ToArray();
        _clearingHosts = true;
        try {
            _hosts.Clear();
            Version++;
            InvalidateBatchObservers();
            var result = Outcome<Exception>.Success;
            foreach (var host in hosts) {
                result = NotifyObservers(OnEntityHostRemoved, host, result);
                result = result.Attempt(host, static (in detached) => detached.Dispose());
            }
            result.ThrowIfFailed();
        }
        finally {
            Count = 0;
            _clearingHosts = false;
        }
    }

    public bool IsClosingHost(IReactiveEntityHost host)
        => IsDisposed || _clearingHosts || ReferenceEquals(_closingHost, host);

    public int ClearEmptyHosts()
    {
        EnsureOwnerThread();
        if (HostCallbackDepth != 0 || Dispatcher.IsSending || Dispatcher.PendingAfterSendActionCount != 0
            || _clearingHosts || _closingHost != null || _initializingHosts.Count != 0) {
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
                    _hosts.Remove(index);
                    Version++;
                    InvalidateBatchObservers();
                    result = NotifyObservers(OnEntityHostRemoved, host, result);
                    result = result.Attempt(host, static (in detached) => detached.Dispose());
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
