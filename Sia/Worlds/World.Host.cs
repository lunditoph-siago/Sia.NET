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
    {
        ref var rawHost = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<WorldEntityHost<TEntity, THost>>.Index, out var exists);
        if (exists) {
            throw new ArgumentException("Host with the same type already exists");
        }
        Version++;
        InvalidateBatchObservers();
        var host = new WorldEntityHost<TEntity, THost>(this);
        rawHost = host;
        InvalidateBatchObservers();
        OnEntityHostAdded?.Invoke(host);
        return host;
    }

    public WorldEntityHost<TEntity, THost> AcquireHost<TEntity, THost>()
        where TEntity : struct, IHList
        where THost : IEntityHost<TEntity>, new()
    {
        ref var rawHost = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<WorldEntityHost<TEntity, THost>>.Index, out var exists);
        if (exists) {
            return Unsafe.As<WorldEntityHost<TEntity, THost>>(rawHost);
        }
        Version++;
        InvalidateBatchObservers();
        var host = new WorldEntityHost<TEntity, THost>(this);
        rawHost = host;
        InvalidateBatchObservers();
        OnEntityHostAdded?.Invoke(host);
        return host;
    }

    public THost UnsafeAddRawHost<THost>(THost host)
        where THost : IReactiveEntityHost
    {
        ref var rawHost = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<THost>.Index, out var exists);
        if (exists) {
            throw new ArgumentException("Host with the same type already exists: " + typeof(THost));
        }
        Version++;
        InvalidateBatchObservers();
        rawHost = host;
        InvalidateBatchObservers();
        OnEntityHostAdded?.Invoke(host);
        return host;
    }

    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        EnsureOwnerThread();
        var hostIndex = WorldEntityHostIndexer<THost>.Index;
        if (!_hosts.TryGetValue(hostIndex, out var rawHost)) {
            return false;
        }
        if (Dispatcher.IsSending) {
            throw new InvalidOperationException("Cannot release a host while sending an event.");
        }

        var host = (THost)rawHost;
        var entities = host.UnsafeGetEntitySpan().ToArray();
        foreach (var entity in entities) {
            if (entity.IsValid && entity.GetStateUnchecked().Releasing) {
                throw new InvalidOperationException("Cannot release a host while an entity is being released.");
            }
        }
        foreach (var entity in entities) {
            if (entity.IsValid) {
                host.Release(entity);
            }
        }
        if (host.Count != 0) {
            throw new InvalidOperationException("Cannot release a host that still contains entities.");
        }

        _hosts.Remove(hostIndex, out var removedHost);
        Version++;
        InvalidateBatchObservers();
        OnEntityHostRemoved?.Invoke(removedHost!);
        host.Dispose();
        return true;
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
        Version++;
        InvalidateBatchObservers();
        var hosts = _hosts.UnsafeRawValues;
        for (var i = 0; i < hosts.Count; ++i) {
            var host = hosts[i];
            OnEntityHostRemoved?.Invoke(host);
            host.Dispose();
        }
        _hosts.Clear();
        InvalidateBatchObservers();
    }

    public int ClearEmptyHosts()
    {
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
            Version++;
            InvalidateBatchObservers();
            try {
                for (var i = 0; i != count; ++i) {
                    _hosts.Remove(hostsToRemove[i], out var host);
                    InvalidateBatchObservers();
                    OnEntityHostRemoved?.Invoke(host!);
                    host!.Dispose();
                }
            }
            finally {
                ArrayPool<int>.Shared.Return(hostsToRemove);
            }
        }

        return count;
    }
}
