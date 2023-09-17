namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class World : IEntityQuery, IEventSender
{
    public class EntityQuery : IEntityQuery
    {
        public World World { get; private set; }
        public IEntityMatcher Matcher { get; private set; }

        private readonly List<IEntityHost> _hosts = new();

        internal EntityQuery(World world, IEntityMatcher matcher)
        {
            World = world;
            Matcher = matcher;

            World.OnEntityHostCreated += OnEntityHostCreated;
            World.OnEntityHostRemoved += OnEntityHostRemoved;

            foreach (var host in world._hosts.AsValueSpan()) {
                if (matcher.Match(host.Descriptor)) {
                    _hosts.Add(host);
                }
            }
        }

        ~EntityQuery()
        {
            DoDispose();
        }

        private void OnEntityHostCreated(IEntityHost host)
        {
            if (Matcher.Match(host.Descriptor)) {
                _hosts.Add(host);
            }
        }

        private void OnEntityHostRemoved(IEntityHost host)
        {
            if (Matcher.Match(host.Descriptor)) {
                _hosts.Remove(host);
            }
        }

        public void ForEach(WorldEntityHandler handler)
        {
            foreach (var host in _hosts) {
                if (host.Count != 0) {
                    IterateHost(host, handler);
                }
            }
        }

        public void ForEach<TData>(in TData data, WorldEntityHandler<TData> handler)
        {
            foreach (var host in _hosts) {
                if (host.Count != 0) {
                    IterateHost(host, data, handler);
                }
            }
        }

        public void Dispose()
        {
            DoDispose();
            GC.SuppressFinalize(this);
        }

        private void DoDispose()
        {
            if (World == null) {
                return;
            }

            World._queries.Remove(Matcher);
            World.OnEntityHostCreated -= OnEntityHostCreated;
            World.OnEntityHostRemoved -= OnEntityHostRemoved;

            World = null!;
            Matcher = null!;
        }
    }

    public event Action<IEntityHost>? OnEntityHostCreated;
    public event Action<IEntityHost>? OnEntityHostRemoved;
    public event Action<World>? OnDisposed;

    public bool IsDisposed { get; private set; }

    public WorldDispatcher Dispatcher { get; }

    public IReadOnlyDictionary<IEntityMatcher, EntityQuery> Queries => _queries;
    public IEnumerable<IEntityHost> EntityHosts => _hosts.Values;
    public IEnumerable<IAddon> Addons => _addons.Values;

    private readonly Dictionary<IEntityMatcher, EntityQuery> _queries = new();
    private readonly SparseSet<IEntityHost> _hosts = new(256, 256);
    private readonly SparseSet<IAddon> _addons = new(256, 256);

    private record struct IterationData(IEntityHost Host, WorldEntityHandler Handler);
    private record struct IterationData<TData>(
        IEntityHost Host, TData Data, WorldEntityHandler<TData> Handler);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IterateHost(IEntityHost host, WorldEntityHandler handler)
    {
        host.IterateAllocated(
            new IterationData(host, handler),
            static (in IterationData data, long pointer) =>
                data.Handler(new(pointer, data.Host)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IterateHost<TData>(
        IEntityHost host, in TData data, WorldEntityHandler<TData> handler)
    {
        host.IterateAllocated(
            new IterationData<TData>(host, data, handler),
            static (in IterationData<TData> data, long pointer) =>
                data.Handler(data.Data, new(pointer, data.Host)));
    }

    public World()
    {
        Dispatcher = new WorldDispatcher(this);
    }

    ~World()
    {
        Dispose(false);
    }

    public void ForEach(WorldEntityHandler handler)
    {
        foreach (var host in _hosts.AsValueSpan()) {
            if (host.Count != 0) {
                IterateHost(host, handler);
            }
        }
    }

    public void ForEach<TData>(in TData data, WorldEntityHandler<TData> handler)
    {
        foreach (var host in _hosts.AsValueSpan()) {
            if (host.Count != 0) {
                IterateHost(host, data, handler);
            }
        }
    }

    public void Query<TTypeUnion>(WorldEntityHandler handler)
        where TTypeUnion : ITypeUnion, new()
        => Query(Matchers.From<TTypeUnion>(), handler);

    public void Query(IEntityMatcher matcher, WorldEntityHandler handler)
    {
        foreach (var host in _hosts.AsValueSpan()) {
            if (host.Count != 0 && matcher.Match(host.Descriptor)) {
                IterateHost(host, handler);
            }
        }
    }

    public void Query<TData>(IEntityMatcher matcher, in TData data, WorldEntityHandler<TData> handler)
    {
        foreach (var host in _hosts.AsValueSpan()) {
            if (host.Count != 0 && matcher.Match(host.Descriptor)) {
                IterateHost(host, data, handler);
            }
        }
    }

    public EntityQuery Query<TTypeUnion>()
        where TTypeUnion : ITypeUnion, new()
        => Query(Matchers.From<TTypeUnion>());

    public EntityQuery Query(IEntityMatcher matcher)
    {
        if (_queries.TryGetValue(matcher, out var query)) {
            return query;
        }
        query = new(this, matcher);
        _queries.Add(matcher, query);
        return query;
    }

    public WorldEntityHost<TEntity, TStorage> GetHost<TEntity, TStorage>()
        where TEntity : struct
        where TStorage : class, IStorage<TEntity>, new()
    {
        ref var host = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<EntityHost<TEntity, TStorage>>.Index, out bool exists);
        if (!exists) {
            host = new WorldEntityHost<TEntity, TStorage>(this, new TStorage());
            OnEntityHostCreated?.Invoke(host);
        }
        return (WorldEntityHost<TEntity, TStorage>)host;
    }

    public WorldEntityHost<TEntity, TStorage> GetHost<TEntity, TStorage>(Func<TStorage> creator)
        where TEntity : struct
        where TStorage : class, IStorage<TEntity>
    {
        ref var host = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<EntityHost<TEntity, TStorage>>.Index, out bool exists);
        if (!exists) {
            host = new WorldEntityHost<TEntity, TStorage>(this, creator());
            OnEntityHostCreated?.Invoke(host);
        }
        return (WorldEntityHost<TEntity, TStorage>)host;
    }

    public WrappedWorldEntityHost<TEntity, THost> GetCustomHost<TEntity, THost>()
        where TEntity : struct
        where THost : IEntityHost<TEntity>, new()
    {
        ref var host = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<THost>.Index, out bool exists);
        if (!exists) {
            var newHost = new WrappedWorldEntityHost<TEntity, THost>(this, new());
            host = newHost;
            OnEntityHostCreated?.Invoke(host);
            return newHost;
        }
        return (WrappedWorldEntityHost<TEntity, THost>)host;
    }

    public WrappedWorldEntityHost<TEntity, THost> GetCustomHost<TEntity, THost>(Func<THost> creator)
        where TEntity : struct
        where THost : IEntityHost<TEntity>
    {
        ref var host = ref _hosts.GetOrAddValueRef(
            WorldEntityHostIndexer<THost>.Index, out bool exists);
        if (!exists) {
            var newHost = new WrappedWorldEntityHost<TEntity, THost>(this, creator());
            host = newHost;
            OnEntityHostCreated?.Invoke(host);
            return newHost;
        }
        return (WrappedWorldEntityHost<TEntity, THost>)host;
    }

    public bool RemoveHost<THost>()
        where THost : IEntityHost
    {
        if (_hosts.Remove(WorldEntityHostIndexer<THost>.Index, out var host)) {
            OnEntityHostRemoved?.Invoke(host);
            return true;
        }
        return false;
    }

    public bool RemoveHost<THost>([MaybeNullWhen(false)] out IEntityHost host)
        where THost : IEntityHost
    {
        if (_hosts.Remove(WorldEntityHostIndexer<THost>.Index, out host)) {
            OnEntityHostRemoved?.Invoke(host);
            return true;
        }
        return false;
    }

    public void Send<TEvent>(in EntityRef target, in TEvent e)
        where TEvent : IEvent
        => Dispatcher.Send(target, e);

    public void Modify<TCommand>(in EntityRef target, in TCommand command)
        where TCommand : ICommand
    {
        command.Execute(this, target);
        Dispatcher.Send(target, command);
    }

    public TAddon AcquireAddon<TAddon>()
        where TAddon : IAddon, new()
    {
        ref var addon = ref _addons.GetOrAddValueRef(
            WorldAddonIndexer<TAddon>.Index, out bool exists);

        if (exists) {
            return (TAddon)addon;
        }

        var newAddon = new TAddon();
        addon = newAddon;
        newAddon.OnInitialize(this);
        return newAddon;
    }

    public TAddon AddAddon<TAddon>()
        where TAddon : IAddon, new()
    {
        ref var singleton = ref _addons.GetOrAddValueRef(
            WorldAddonIndexer<TAddon>.Index, out bool exists);

        if (exists) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }

        var newSingleton = new TAddon();
        singleton = newSingleton;
        newSingleton.OnInitialize(this);
        return newSingleton;
    }

    public bool RemoveAddon<TAddon>()
        where TAddon : IAddon
    {
        if (_addons.Remove(WorldAddonIndexer<TAddon>.Index, out var addon)) {
            addon.OnUninitialize(this);
            return true;
        }
        return false;
    }

    public TAddon GetAddon<TAddon>()
        where TAddon : IAddon
    {
        ref var addon = ref _addons.GetValueRefOrNullRef(
            WorldAddonIndexer<TAddon>.Index);

        if (Unsafe.IsNullRef(ref addon)) {
            throw new Exception("Addon not found: " + typeof(TAddon));
        }
        return (TAddon)addon;
    }

    public bool ContainsAddon<TAddon>()
        where TAddon : IAddon
        => _addons.ContainsKey(WorldAddonIndexer<TAddon>.Index);

    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed) { return; }
        IsDisposed = true;
        OnDisposed?.Invoke(this);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}