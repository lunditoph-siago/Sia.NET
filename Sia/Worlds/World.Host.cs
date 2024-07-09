namespace Sia;

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public partial class World
{
    public event Action<IReactiveEntityHost>? OnEntityHostAdded;
    public event Action<IReactiveEntityHost>? OnEntityHostRemoved;

    public IReadOnlyList<IReactiveEntityHost> Hosts => _hosts.UnsafeRawValues;
    IReadOnlyList<IEntityHost> IEntityQuery.Hosts => _hosts.UnsafeRawValues;

    private readonly SparseSet<IReactiveEntityHost> _hosts = [];

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
            WorldEntityHostIndexer<WorldEntityHost<TEntity, THost>>.Index, out bool exists);
        if (exists) {
            throw new ArgumentException("Host with the same type already exists");
        }
        Version++;
        var host = new WorldEntityHost<TEntity, THost>(this);
        OnEntityHostAdded?.Invoke(host);
        rawHost = host;
        return host;
    }

    public WorldEntityHost<TEntity, THost> AcquireHost<TEntity, THost>()
        where TEntity : struct, IHList
        where THost : IEntityHost<TEntity>, new()
    {
        ref var rawHost = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<WorldEntityHost<TEntity, THost>>.Index, out bool exists);
        if (exists) {
            return Unsafe.As<WorldEntityHost<TEntity, THost>>(rawHost);
        }
        Version++;
        var host = new WorldEntityHost<TEntity, THost>(this);
        OnEntityHostAdded?.Invoke(host);
        rawHost = host;
        return host;
    }

    public THost UnsafeAddRawHost<THost>(THost host)
        where THost : IReactiveEntityHost
    {
        ref var rawHost = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<THost>.Index, out bool exists);
        if (exists) {
            throw new ArgumentException("Host with the same type already exists: " + typeof(THost));
        }
        Version++;
        rawHost = host;
        OnEntityHostAdded?.Invoke(host);
        return host;
    }

    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        if (_hosts.Remove(WorldEntityHostIndexer<THost>.Index, out var host)) {
            Version++;
            OnEntityHostRemoved?.Invoke(host);
            host.Dispose();
            return true;
        }
        return false;
    }

    public bool TryGetHost<THost>([MaybeNullWhen(false)] out THost host)
        where THost : IEntityHost
    {
        if (_hosts.TryGetValue(WorldEntityHostIndexer<THost>.Index, out var rawHost)) {
            host = (THost)rawHost;
            return true;
        }
        host = default;
        return false;
    }

    public bool ConainsHost<THost>()
        where THost : IEntityHost
        => _hosts.ContainsKey(WorldEntityHostIndexer<THost>.Index);
    
    public void ClearHosts()
    {
        Version++;
        var hosts = _hosts.UnsafeRawValues;
        for (int i = 0; i < hosts.Count; ++i) {
            var host = hosts[i];
            OnEntityHostRemoved?.Invoke(host);
            host.Dispose();
        }
        _hosts.Clear();
    }

    public int ClearEmptyHosts()
    {
        int[]? hostsToRemove = null;
        int count = 0;

        foreach (var (key, host) in _hosts) {
            if (host.Count == 0) {
                hostsToRemove ??= ArrayPool<int>.Shared.Rent(_hosts.Count);
                hostsToRemove[count] = key;
                count++;
            }
        }

        if (hostsToRemove != null) {
            Version++;
            try {
                for (int i = 0; i != count; ++i) {
                    _hosts.Remove(hostsToRemove[i], out var host);
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