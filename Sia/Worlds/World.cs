namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public sealed partial class World : IEntityQuery, IEventSender
{
    public event Action<IReactiveEntityHost>? OnEntityHostCreated;
    public event Action<IReactiveEntityHost>? OnEntityHostReleased;
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
    public void Start(Action action)
    {
        Context<World>.With(this, action);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query<TTypeUnion>(EntityHandler handler)
        where TTypeUnion : ITypeUnion, new()
        => Query(Matchers.From<TTypeUnion>(), handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Query<TTypeUnion>(SimpleEntityHandler handler)
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
    public void Query(IEntityMatcher matcher, SimpleEntityHandler handler)
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
    public void Query<TData>(IEntityMatcher matcher, in TData data, SimpleEntityHandler<TData> handler)
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
    public EntityQuery Query<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => Query(Matchers.From<TTypeUnion>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityQuery Query(IEntityMatcher matcher)
    {
        if (_queries.TryGetValue(matcher, out var query)) {
            return query;
        }
        query = new(this, matcher);
        _queries.Add(matcher, query);
        return query;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldEntityHost<TEntity, TStorage> GetHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, TStorage>()
        where TEntity : struct
        where TStorage : class, IStorage<TEntity>, new()
        => GetHost<TEntity, TStorage>(static () => new());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldEntityHost<TEntity, TStorage> GetHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, TStorage>(Func<TStorage> creator)
        where TEntity : struct
        where TStorage : class, IStorage<TEntity>
    {
        ref var host = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<WorldEntityHost<TEntity, TStorage>>.Index, out bool exists);
        if (!exists) {
            host = new WorldEntityHost<TEntity, TStorage>(this, creator());
            OnEntityHostCreated?.Invoke(host);
        }
        return (WorldEntityHost<TEntity, TStorage>)host;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WrappedWorldEntityHost<TEntity, THost> GetCustomHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, THost>()
        where TEntity : struct
        where THost : IEntityHost<TEntity>, new()
        => GetCustomHost<TEntity, THost>(static () => new());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WrappedWorldEntityHost<TEntity, THost> GetCustomHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, THost>(Func<THost> creator)
        where TEntity : struct
        where THost : IEntityHost<TEntity>
    {
        ref var host = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<THost>.Index, out bool exists);
        if (!exists) {
            var newHost = new WrappedWorldEntityHost<TEntity, THost>(this, creator());
            OnEntityHostCreated?.Invoke(newHost);
            return newHost;
        }
        return (WrappedWorldEntityHost<TEntity, THost>)host;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        if (_hosts.Remove(WorldEntityHostIndexer<THost>.Index, out var host)) {
            OnEntityHostReleased?.Invoke(host);
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
            OnEntityHostReleased?.Invoke(host);
            host.Dispose();
        }
        _hosts.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send<TEvent>(in EntityRef target, in TEvent e)
        where TEvent : IEvent
        => Dispatcher.Send(target, e);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send<TEntity, TEvent>(in EntityRef<TEntity> target, in TEvent e)
        where TEntity : struct
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
    public void Modify<TEntity, TCommand>(in EntityRef<TEntity> target, in TCommand command)
        where TEntity : struct
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
    public void Modify<TEntity, TComponent, TCommand>(
        in EntityRef<TEntity> target, ref TComponent component, in TCommand command)
        where TEntity : struct
        where TCommand : ICommand<TComponent>
    {
        command.Execute(this, target, ref component);
        Dispatcher.Send(target, command);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldCommandBuffer CreateCommandBuffer()
        => new(this);

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
        addon.OnUninitialize(this);
        addon = null;
        _addonCount--;
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
                addonAcc++;
            }
        }
        _addonCount = 0;
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