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
    internal int ActiveReleaseCount;

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
            return PublishHost(index, create(this));
        }
        finally {
            _hostOperations.Remove(index);
        }
    }

    public THost UnsafeAddRawHost<THost>(THost host)
        where THost : IReactiveEntityHost
    {
        EnsureHostRegistration();
        var index = WorldEntityHostIndexer<THost>.Index;
        BeginHostOperation(index);
        try {
            return PublishHost(index, host);
        }
        finally {
            _hostOperations.Remove(index);
        }
    }

    private THost PublishHost<THost>(int index, THost host) where THost : IReactiveEntityHost
    {
        IReactiveEntityHost registered = host;
        ArgumentNullException.ThrowIfNull(registered, nameof(host));
        if (!_hosts.Add(index, registered)) {
            throw new ArgumentException("Host with the same type already exists: " + typeof(THost));
        }
        Version++;
        InvalidateBatchObservers();
        NotifyObservers(OnEntityHostAdded, registered, Outcome<Exception>.Success).ThrowIfFailed();
        return host;
    }

    private void EnsureHostRegistration()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_clearingHosts) throw new InvalidOperationException("Cannot register a host while clearing hosts.");
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
                if (!entity.IsValid) continue;
                var slot = entity.GetStateUnchecked().Slot;
                var current = host.UnsafeGetEntitySpan();
                if ((uint)slot < (uint)current.Length && current[slot] == entity) host.Release(entity);
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
        if (ActiveReleaseCount != 0 || Dispatcher.PendingAfterSendActionCount != 0) {
            foreach (var host in _hosts.UnsafeRawValues) EnsureNoEntityRelease(host.UnsafeGetEntitySpan());
        }
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
        result = result.Attempt((World: this, Host: host), static (in context) => {
            var dispatcher = context.World.Dispatcher;
            var clearsSinksOnDispose = !dispatcher.IsSending && context.Host is IWorldBatchHost { IsExactArrayHost: true };
            foreach (var entity in context.Host.UnsafeGetEntitySpan()) {
                if (clearsSinksOnDispose && dispatcher.HasNoUntypedListeners) break;
                dispatcher.DetachEntityListeners(entity);
            }
            if (context.Host is IWorldBatchHost) context.World.Count -= context.Host.Count;
        });
        return result.Attempt(host, static (in detached) => detached.Dispose());
    }

    public int ClearEmptyHosts()
    {
        EnsureOwnerThread();
        EnsureHostTeardown();
        var pool = ArrayPool<(int Index, IReactiveEntityHost Host)>.Shared;
        (int Index, IReactiveEntityHost Host)[]? candidates = null;
        var count = 0;
        var removed = 0;
        var result = Outcome<Exception>.Success;
        try {
            foreach (var (index, host) in _hosts) {
                if (_hostOperations.Contains(index) || host.Count != 0) continue;
                candidates ??= pool.Rent(_hosts.Count);
                candidates[count++] = (index, host);
            }
            if (candidates is null) return 0;
            for (var i = 0; i < count; ++i) {
                var (index, candidate) = candidates[i];
                if (!_hosts.TryGetValue(index, out var host) || !ReferenceEquals(candidate, host)
                    || _hostOperations.Contains(index) || host.Count != 0) continue;
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
            if (candidates is { }) pool.Return(candidates, clearArray: true);
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
