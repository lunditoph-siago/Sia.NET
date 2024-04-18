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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetHost<TEntity, TStorage>([MaybeNullWhen(false)] out WorldEntityHost<TEntity, TStorage> host)
        where TEntity : IHList
        where TStorage : IStorage<HList<Identity, TEntity>>
    {
        ref var rawHost = ref _hosts.GetValueRefOrNullRef(
            WorldEntityHostIndexer<WorldEntityHost<TEntity, TStorage>>.Index);
        if (Unsafe.IsNullRef(ref rawHost)) {
            host = null;
            return false;
        }
        host = Unsafe.As<WorldEntityHost<TEntity, TStorage>>(rawHost);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldEntityHost<TEntity, TStorage> AddHost<TEntity, TStorage>(
        Func<World, (TStorage Storage, IEntityHostProvider SiblingHostProvider)> creator)
        where TEntity : IHList
        where TStorage : IStorage<HList<Identity, TEntity>>
    {
        ref var rawHost = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<WorldEntityHost<TEntity, TStorage>>.Index, out bool exists);
        if (exists) {
            throw new ArgumentException("Host with the same type already exists");
        }
        var (storage, siblingHostProvider) = creator(this);
        var host = new WorldEntityHost<TEntity, TStorage>(this, storage, siblingHostProvider);
        OnEntityHostAdded?.Invoke(host);
        rawHost = host;
        return host;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCustomHost<TEntity, THost>([MaybeNullWhen(false)] out WrappedWorldEntityHost<TEntity, THost> host)
        where TEntity : IHList
        where THost : IEntityHost<TEntity>
    {
        ref var rawHost = ref _hosts.GetValueRefOrNullRef(
            WorldEntityHostIndexer<THost>.Index);
        if (Unsafe.IsNullRef(ref rawHost)) {
            host = null;
            return false;
        }
        host = Unsafe.As<WrappedWorldEntityHost<TEntity, THost>>(rawHost);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WrappedWorldEntityHost<TEntity, THost> AddCustomHost<TEntity, THost>(Func<World, THost> creator)
        where TEntity : IHList
        where THost : IEntityHost<TEntity>
    {
        ref var rawHost = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<THost>.Index, out bool exists);
        if (exists) {
            throw new ArgumentException("Host with the same type already exists");
        }
        var host = new WrappedWorldEntityHost<TEntity, THost>(this, creator(this));
        OnEntityHostAdded?.Invoke(host);
        rawHost = host;
        return host;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        if (_hosts.Remove(WorldEntityHostIndexer<THost>.Index, out var host)) {
            OnEntityHostRemoved?.Invoke(host);
            host.Dispose();
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConainsHost<THost>()
        where THost : IEntityHost
        => _hosts.ContainsKey(WorldEntityHostIndexer<THost>.Index);
    
    public void ClearHosts()
    {
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