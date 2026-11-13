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
    private readonly HashSet<int> _hostOperations = [];
    private bool _clearingHosts;
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
        => GetOrCreateHost<WorldEntityHost<TEntity, THost>>(static world => new(world), acquire: false);

    public WorldEntityHost<TEntity, THost> AcquireHost<TEntity, THost>()
        where TEntity : struct, IHList
        where THost : IEntityHost<TEntity>, new()
        => GetOrCreateHost<WorldEntityHost<TEntity, THost>>(static world => new(world), acquire: true);

    internal THost AcquireRawHost<THost>(Func<World, THost> create)
        where THost : IReactiveEntityHost
        => GetOrCreateHost(create, acquire: true);

    private THost GetOrCreateHost<THost>(Func<World, THost> create, bool acquire)
        where THost : IReactiveEntityHost
    {
        EnsureHostRegistration();
        var index = WorldEntityHostIndexer<THost>.Index;
        if (_hosts.TryGetValue(index, out var existing)) {
            if (acquire) return (THost)existing;
            throw new ArgumentException("Host with the same type already exists: " + typeof(THost));
        }
        BeginHostOperation(index);
        THost host;
        try {
            host = create(this);
            RegisterHost(index, host);
            NotifyObservers(OnEntityHostAdded, host, Outcome<Exception>.Success).ThrowIfFailed();
        }
        finally {
            _hostOperations.Remove(index);
        }
        return host;
    }

    public THost UnsafeAddRawHost<THost>(THost host)
        where THost : IReactiveEntityHost
    {
        EnsureHostRegistration();
        ArgumentNullException.ThrowIfNull(host);
        var index = WorldEntityHostIndexer<THost>.Index;
        BeginHostOperation(index);
        try {
            RegisterHost(index, host);
            NotifyObservers(OnEntityHostAdded, host, Outcome<Exception>.Success).ThrowIfFailed();
        }
        finally {
            _hostOperations.Remove(index);
        }
        return host;
    }

    private void RegisterHost(int index, IReactiveEntityHost host)
    {
        if (!_hosts.Add(index, host))
            throw new ArgumentException("Host with the same type already exists: " + host.GetType());
        Version++;
        InvalidateBatchObservers();
    }

    private void EnsureHostRegistration()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_clearingHosts)
            throw new InvalidOperationException("Cannot register a host while the world is clearing hosts.");
    }

    private void BeginHostOperation(int index)
    {
        if (!_hostOperations.Add(index))
            throw new InvalidOperationException("A host of the same type is already being initialized or removed.");
    }

    private void EnsureHostTeardown()
    {
        EnsureNoHostMigration();
        if (Dispatcher.IsSending)
            throw new InvalidOperationException("Cannot remove a host while sending an event.");
        if (_clearingHosts)
            throw new InvalidOperationException("Cannot remove a host while the world is clearing hosts.");
    }

    private static void EnsureNoEntityRelease(IReactiveEntityHost host)
    {
        foreach (var entity in host.UnsafeGetEntitySpan()) {
            if (entity.IsValid && entity.GetStateUnchecked().Releasing)
                throw new InvalidOperationException("Cannot release a host while an entity is being released.");
        }
    }

    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        EnsureOwnerThread();
        var hostIndex = WorldEntityHostIndexer<THost>.Index;
        if (!_hosts.TryGetValue(hostIndex, out var rawHost)) {
            return false;
        }
        EnsureHostTeardown();
        EnsureNoEntityRelease(rawHost);
        BeginHostOperation(hostIndex);
        try {
            var entities = rawHost.UnsafeGetEntitySpan().ToArray();
            foreach (var entity in entities) {
                if (entity.IsValid && ReferenceEquals(entity.Host, rawHost)) rawHost.Release(entity);
            }
            if (rawHost.Count != 0) {
                throw new InvalidOperationException("Cannot release a host that still contains entities.");
            }

            _hosts.Remove(hostIndex);
            Version++;
            InvalidateBatchObservers();
            DisposeDetachedHost(rawHost, Outcome<Exception>.Success).ThrowIfFailed();
            return true;
        }
        finally {
            _hostOperations.Remove(hostIndex);
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
        EnsureCanClearHosts();
        ClearHostsCore();
    }

    private void EnsureCanClearHosts()
    {
        EnsureHostTeardown();
        if (_hostOperations.Count != 0)
            throw new InvalidOperationException("Cannot clear hosts while a host is being initialized or removed.");
        foreach (var host in Hosts) EnsureNoEntityRelease(host);
    }

    private void ClearHostsCore()
    {
        var hosts = _hosts.UnsafeRawValues.ToArray();
        _clearingHosts = true;
        var result = Outcome<Exception>.Success;
        try {
            foreach (var host in hosts)
                if (host is IWorldBatchHost owned) owned.CloseForCreation();
            _hosts.Clear();
            Version++;
            InvalidateBatchObservers();
            foreach (var host in hosts) result = DisposeDetachedHost(host, result);
        }
        finally {
            Count = 0;
            _clearingHosts = false;
        }
        result.ThrowIfFailed();
    }

    private Outcome<Exception> DisposeDetachedHost(IReactiveEntityHost host, Outcome<Exception> result)
    {
        var owned = host as IWorldBatchHost;
        owned?.CloseForCreation();
        result = NotifyObservers(OnEntityHostRemoved, host, result);
        // Removal observers may release entities. The remaining entries are
        // released by host disposal without the ordinary per-entity notifications.
        result = result.Attempt(() => {
            foreach (var entity in host.UnsafeGetEntitySpan())
                Dispatcher.DetachEntityListeners(entity);
        });
        if (owned is not null) result = result.Attempt(() => Count -= host.Count);
        return result.Attempt(host.Dispose);
    }

    public int ClearEmptyHosts()
    {
        EnsureOwnerThread();
        EnsureHostTeardown();
        List<(int Index, IReactiveEntityHost Host)>? candidates = null;
        foreach (var (key, host) in _hosts) {
            if (host.Count == 0 && !_hostOperations.Contains(key))
                (candidates ??= []).Add((key, host));
        }

        if (candidates is null) return 0;
        var count = 0;
        var result = Outcome<Exception>.Success;
        foreach (var (index, host) in candidates) {
            if (!_hosts.TryGetValue(index, out var current) || !ReferenceEquals(current, host)
                || host.Count != 0 || _hostOperations.Contains(index)) continue;
            BeginHostOperation(index);
            try {
                _hosts.Remove(index);
                Version++;
                InvalidateBatchObservers();
                count++;
                result = DisposeDetachedHost(host, result);
            }
            finally {
                _hostOperations.Remove(index);
            }
        }
        result.ThrowIfFailed();
        return count;
    }
}
