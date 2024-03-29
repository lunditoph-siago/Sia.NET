namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public sealed partial class World : IReactiveEntityQuery, IEventSender
{
    public static World Current => Context.Get<World>();

    public event Action<IReactiveEntityHost>? OnEntityHostAdded;
    public event Action<IReactiveEntityHost>? OnEntityHostRemoved;

    public event Action<IAddon>? OnAddonCreated;
    public event Action<IAddon>? OnAddonRemoved;

    public event Action<World>? OnDisposed;

    public int Count { get; internal set; }

    public bool IsDisposed { get; private set; }

    public WorldDispatcher Dispatcher { get; }

    public IReadOnlyDictionary<IEntityMatcher, EntityQuery> Queries => _queries;
    public IReadOnlyList<IReactiveEntityHost> Hosts => _hosts.UnsafeRawValues;

    IReadOnlyList<IEntityHost> IEntityQuery.Hosts => _hosts.UnsafeRawValues;

    public IEnumerable<IAddon> Addons {
        get {
            for (int i = 0; i < _addonCount;) {
                var addon = _addons[i];
                if (addon != null) {
                    i++;
                    yield return addon;
                }
            }
        }
    }

    internal readonly Dictionary<IEntityMatcher, EntityQuery> _queries = [];
    private readonly SparseSet<IReactiveEntityHost> _hosts = [];

    private readonly IAddon?[] _addons = new IAddon?[2048];
    private int _addonCount = 0;

    public World()
    {
        Dispatcher = new WorldDispatcher(this);
    }

    ~World()
    {
        Dispose(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query<TTypeUnion>(EntityHandler handler)
        where TTypeUnion : ITypeUnion, new()
        => Query(Matchers.From<TTypeUnion>(), handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query(IEntityMatcher matcher, EntityHandler handler)
    {
        foreach (var host in _hosts.ValueSpan) {
            if (host.Count != 0 && matcher.Match(host)) {
                foreach (var entity in host) {
                    handler(entity);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query<TData>(IEntityMatcher matcher, in TData data, EntityHandler<TData> handler)
    {
        var hosts = _hosts.UnsafeRawValues;
        for (int i = 0; i != hosts.Count; ++i) {
            var host = hosts[i];
            if (host.Count != 0 && matcher.Match(host)) {
                foreach (var entity in host) {
                    handler(data, entity);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReactiveEntityQuery Query<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => Query(Matchers.From<TTypeUnion>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReactiveEntityQuery Query(IEntityMatcher matcher)
    {
        if (matcher == Matchers.Any) {
            return this;
        }
        if (_queries.TryGetValue(matcher, out var query)) {
            return query;
        }
        query = new(this, matcher);
        _queries.Add(matcher, query);
        return query;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send<TEvent>(in EntityRef target, in TEvent e)
        where TEvent : IEvent
        => Dispatcher.Send(target, e);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Modify<TCommand>(in EntityRef target, in TCommand command)
        where TCommand : ICommand
    {
        command.Execute(this, target);
        Dispatcher.Send(target, command);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Modify<TComponent, TCommand>(
        in EntityRef target, ref TComponent component, in TCommand command)
        where TCommand : ICommand<TComponent>
    {
        command.Execute(this, target, ref component);
        Dispatcher.Send(target, command);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AcquireAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        ref var addon = ref _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon != null) {
            return Unsafe.As<TAddon>(addon);
        }
        var newAddon = CreateAddon<TAddon>();
        addon = newAddon;
        OnAddonCreated?.Invoke(newAddon);
        return newAddon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon AddAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        ref var addon = ref _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon != null) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }
        var newAddon = CreateAddon<TAddon>();
        addon = newAddon;
        OnAddonCreated?.Invoke(newAddon);
        return newAddon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TAddon CreateAddon<TAddon>()
        where TAddon : class, IAddon, new()
    {
        var addon = new TAddon();
        addon.OnInitialize(this);
        _addonCount++;
        return addon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool RemoveAddon<TAddon>()
        where TAddon : class, IAddon
    {
        ref var addon = ref _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon == null) {
            return false;
        }
        var removedAddon = addon;
        addon.OnUninitialize(this);
        addon = null;
        _addonCount--;
        OnAddonRemoved?.Invoke(removedAddon);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TAddon GetAddon<TAddon>()
        where TAddon : class, IAddon
    {
        var addon = _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon != null) {
            return Unsafe.As<TAddon>(addon);
        }

        for (int i = 0, addonAcc = 0; addonAcc < _addonCount; ++i) {
            addon = _addons[i];
            if (addon != null) {
                if (addon is TAddon converted) {
                    return converted;
                }
                addonAcc++;
            }
        }

        throw new KeyNotFoundException("Addon not found: " + typeof(TAddon));
    }

    public IEnumerable<TAddon> GetAddons<TAddon>()
        where TAddon : class, IAddon
    {
        var addon = _addons[WorldAddonIndexer<TAddon>.Index];
        if (addon != null) {
            yield return Unsafe.As<TAddon>(addon);
        }

        for (int i = 0, addonAcc = 0; addonAcc < _addonCount; ++i) {
            addon = _addons[i];
            if (addon != null) {
                if (addon is TAddon converted) {
                    yield return converted;
                }
                addonAcc++;
            }
        }
    }
    
    public bool TryGetAddon<TAddon>([MaybeNullWhen(false)] out TAddon addon)
        where TAddon : class, IAddon
    {
        var rawAddon = _addons[WorldAddonIndexer<TAddon>.Index];

        if (rawAddon != null) {
            addon = Unsafe.As<TAddon>(rawAddon);
            return true;
        }

        for (int i = 0, addonAcc = 0; addonAcc < _addonCount; ++i) {
            rawAddon = _addons[i];
            if (rawAddon != null) {
                if (rawAddon is TAddon converted) {
                    addon = converted;
                    return true;
                }
                addonAcc++;
            }
        }

        addon = default;
        return false;
    }

    public void ClearAddons()
    {
        for (int i = 0, addonAcc = 0; addonAcc < _addonCount; ++i) {
            var addon = _addons[i];
            if (addon != null) {
                addon.OnUninitialize(this);
                _addons[i] = null;
                _addonCount--;
                OnAddonRemoved?.Invoke(addon);
                addonAcc++;
            }
        }
    }

    private void Dispose(bool disposing)
    {
        if (IsDisposed) { return; }
        IsDisposed = true;

        ClearHosts();
        ClearAddons();

        OnDisposed?.Invoke(this);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}