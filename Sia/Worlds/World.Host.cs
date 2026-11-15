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
    private readonly HashSet<int> _hostOperations = [];
    private readonly object _batchObserverLock = new();
    private bool? _hasNoBatchObservers;
    private bool _clearingHosts;
    private int _hostMigrationDepth;

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
        if (IsNotifyingHostMigration) return false;

        lock (_batchObserverLock) {
            if (_hasNoBatchObservers is { } cached) return cached;
            foreach (var host in _hosts.UnsafeRawValues) {
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

    internal THost AcquireRawHost<THost>(Func<World, THost> create)
        where THost : IReactiveEntityHost
        => RegisterHost(create, acquire: true);

    private THost RegisterHost<THost>(Func<World, THost> create, bool acquire)
        where THost : IReactiveEntityHost
    {
        EnsureHostRegistration();
        var index = WorldEntityHostIndexer<THost>.Index;
        if (_hosts.TryGetValue(index, out var existing)) {
            if (acquire) return (THost)existing;
            throw new ArgumentException("Host with the same type already exists: " + typeof(THost));
        }

        BeginHostOperation(index);
        try {
            var host = create(this);
            IReactiveEntityHost registered = host;
            _hosts[index] = registered;
            NotifyHostAdded(registered);
            return host;
        }
        finally {
            _hostOperations.Remove(index);
        }
    }

    public THost UnsafeAddRawHost<THost>(THost host)
        where THost : IReactiveEntityHost
    {
        EnsureHostRegistration();
        IReactiveEntityHost registered = host;
        ArgumentNullException.ThrowIfNull(registered, nameof(host));
        var index = WorldEntityHostIndexer<THost>.Index;
        BeginHostOperation(index);
        try {
            if (!_hosts.Add(index, registered)) {
                throw new ArgumentException("Host with the same type already exists: " + typeof(THost));
            }
            NotifyHostAdded(registered);
            return host;
        }
        finally {
            _hostOperations.Remove(index);
        }
    }

    private void NotifyHostAdded(IReactiveEntityHost host)
    {
        Version++;
        InvalidateBatchObservers();
        NotifyObservers(OnEntityHostAdded, host, Outcome<Exception>.Success).ThrowIfFailed();
    }

    private void EnsureHostRegistration()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_clearingHosts) throw new InvalidOperationException("Cannot register a host while the world is clearing hosts.");
    }

    private void BeginHostOperation(int index)
    {
        if (!_hostOperations.Add(index)) {
            throw new InvalidOperationException("The host is already being initialized or removed.");
        }
    }

    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        EnsureOwnerThread();
        var hostIndex = WorldEntityHostIndexer<THost>.Index;
        if (!_hosts.TryGetValue(hostIndex, out var host)) return false;
        EnsureHostTeardown();
        BeginHostOperation(hostIndex);
        try {
            var entities = host.UnsafeGetEntitySpan().ToArray();
            EnsureNoEntityRelease(entities);
            foreach (var entity in entities) {
                if (entity.IsValid && ReferenceEquals(entity.Host, host)) host.Release(entity);
            }
            if (host.Count != 0) {
                throw new InvalidOperationException("Cannot release a host that still contains entities.");
            }

            RemoveHost(hostIndex, host, Outcome<Exception>.Success).ThrowIfFailed();
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

    private void EnsureHostTeardown()
    {
        EnsureNoHostMigration();
        if (_clearingHosts || Dispatcher.IsSending) {
            throw new InvalidOperationException("Cannot remove a host during host teardown or event dispatch.");
        }
    }

    private void EnsureCanClearHosts()
    {
        EnsureHostTeardown();
        if (_hostOperations.Count != 0) {
            throw new InvalidOperationException("Cannot clear hosts during host initialization or removal.");
        }
        foreach (var host in _hosts.UnsafeRawValues) EnsureNoEntityRelease(host.UnsafeGetEntitySpan());
    }

    private static void EnsureNoEntityRelease(ReadOnlySpan<Entity> entities)
    {
        foreach (var entity in entities) {
            if (entity.GetStateUnchecked() is { Releasing: true, Host: not null } state && entity.References(state)) {
                throw new InvalidOperationException("Cannot remove a host while an entity is being released.");
            }
        }
    }

    private void ClearHostsCore()
    {
        var hosts = _hosts.UnsafeRawValues.ToArray();
        _clearingHosts = true;
        try {
            foreach (var host in hosts) (host as IWorldBatchHost)?.CloseForCreation();
            _hosts.Clear();
            Version++;
            InvalidateBatchObservers();

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
        (host as IWorldBatchHost)?.CloseForCreation();
        Version++;
        InvalidateBatchObservers();
        return DisposeDetachedHost(host, result);
    }

    private Outcome<Exception> DisposeDetachedHost(IReactiveEntityHost host, Outcome<Exception> result)
    {
        result = NotifyObservers(OnEntityHostRemoved, host, result);
        result = result.Attempt((this, host), static (in (World World, IReactiveEntityHost Host) state) => {
            foreach (var entity in state.Host.UnsafeGetEntitySpan()) state.World.Dispatcher.DetachEntityListeners(entity);
            if (state.Host is IWorldBatchHost) state.World.Count -= state.Host.Count;
        });
        return result.Attempt(host, static (in IReactiveEntityHost detached) => detached.Dispose());
    }

    public int ClearEmptyHosts()
    {
        EnsureOwnerThread();
        EnsureHostTeardown();
        var pool = ArrayPool<(int Index, IReactiveEntityHost Host)>.Shared;
        (int Index, IReactiveEntityHost Host)[]? hostsToRemove = null;
        var count = 0;

        foreach (var (key, host) in _hosts) {
            if (host.Count == 0 && !_hostOperations.Contains(key)) {
                hostsToRemove ??= pool.Rent(_hosts.Count);
                hostsToRemove[count++] = (key, host);
            }
        }

        if (hostsToRemove is null) return 0;

        var removed = 0;
        var result = Outcome<Exception>.Success;
        try {
            for (var i = 0; i < count; ++i) {
                var (index, candidate) = hostsToRemove[i];
                if (!_hosts.TryGetValue(index, out var host) || !ReferenceEquals(candidate, host)
                    || host.Count != 0 || _hostOperations.Contains(index)) continue;

                BeginHostOperation(index);
                try {
                    result = RemoveHost(index, host, result);
                    removed++;
                }
                finally {
                    _hostOperations.Remove(index);
                }
            }
            result.ThrowIfFailed();
            return removed;
        }
        finally {
            pool.Return(hostsToRemove, clearArray: true);
        }
    }

    internal bool IsNotifyingHostMigration => _hostMigrationDepth != 0;

    internal void BeginHostMigration() => _hostMigrationDepth++;
    internal void EndHostMigration() => _hostMigrationDepth--;

    internal void EnsureNoHostMigration()
    {
        if (IsNotifyingHostMigration) {
            throw new InvalidOperationException("Cannot dispose or remove hosts during entity migration.");
        }
    }
}
