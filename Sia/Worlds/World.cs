namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class World : IEntityQuery, IEventSender
{
    public class EntityQuery : IEntityQuery
    {
        public event Action<IReactiveEntityHost>? OnEntityHostAdded;
        public event Action<IReactiveEntityHost>? OnEntityHostRemoved;

        public int Count {
            get {
                int count = 0;
                foreach (var host in CollectionsMarshal.AsSpan(_hosts)) {
                    count += host.Count;
                }
                return count;
            }
        }

        public World World { get; private set; }
        public IEntityMatcher Matcher { get; private set; }

        public IReadOnlyList<IReactiveEntityHost> Hosts => _hosts;

        private readonly List<IReactiveEntityHost> _hosts = new();

        internal EntityQuery(World world, IEntityMatcher matcher)
        {
            World = world;
            Matcher = matcher;

            World.OnEntityHostCreated += OnEntityHostCreated;
            World.OnEntityHostReleased += OnEntityHostReleased;

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

        private void OnEntityHostCreated(IReactiveEntityHost host)
        {
            if (Matcher.Match(host.Descriptor)) {
                _hosts.Add(host);
                OnEntityHostAdded?.Invoke(host);
            }
        }

        private void OnEntityHostReleased(IReactiveEntityHost host)
        {
            if (Matcher.Match(host.Descriptor)) {
                _hosts.Remove(host);
                OnEntityHostRemoved?.Invoke(host);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach(EntityHandler handler)
        {
            foreach (var host in _hosts) {
                if (host.Count != 0) {
                    IterateHost(host, handler);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach(SimpleEntityHandler handler)
        {
            foreach (var host in _hosts) {
                if (host.Count != 0) {
                    IterateHost(host, handler);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<TData>(in TData data, EntityHandler<TData> handler)
        {
            foreach (var host in _hosts) {
                if (host.Count != 0) {
                    IterateHost(host, data, handler);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach<TData>(in TData data, SimpleEntityHandler<TData> handler)
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
            World.OnEntityHostReleased -= OnEntityHostReleased;

            World = null!;
            Matcher = null!;
        }
    }

    private readonly record struct IterationData(IEntityHost Host, EntityHandler Handler);
    private readonly record struct SimpleIterationData(IEntityHost Host, SimpleEntityHandler Handler);
    private readonly record struct IterationData<TData>(
        IEntityHost Host, TData Data, EntityHandler<TData> Handler);
    private readonly record struct SimpleIterationData<TData>(
        IEntityHost Host, TData Data, SimpleEntityHandler<TData> Handler);

    public event Action<IReactiveEntityHost>? OnEntityHostCreated;
    public event Action<IReactiveEntityHost>? OnEntityHostReleased;
    public event Action<World>? OnDisposed;

    public int Count { get; internal set; }

    public bool IsDisposed { get; private set; }

    public WorldDispatcher Dispatcher { get; }

    public IReadOnlyDictionary<IEntityMatcher, EntityQuery> Queries => _queries;
    public IEnumerable<IEntityHost> EntityHosts => _hosts.Values;
    public IEnumerable<IAddon> Addons => _addons.Values;

    private readonly Dictionary<IEntityMatcher, EntityQuery> _queries = new();
    private readonly SparseSet<IReactiveEntityHost> _hosts = new(256, 256);
    private readonly Dictionary<int, IAddon> _addons = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IterateHost(IEntityHost host, EntityHandler handler)
    {
        host.IterateAllocated(
            new IterationData(host, handler),
            static (in IterationData data, long pointer) =>
                data.Handler(new(pointer, data.Host)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IterateHost(IEntityHost host, SimpleEntityHandler handler)
    {
        host.IterateAllocated(
            new SimpleIterationData(host, handler),
            static (in SimpleIterationData data, long pointer) =>
                data.Handler(new(pointer, data.Host)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IterateHost<TData>(
        IEntityHost host, in TData data, EntityHandler<TData> handler)
    {
        host.IterateAllocated(
            new IterationData<TData>(host, data, handler),
            static (in IterationData<TData> data, long pointer) =>
                data.Handler(data.Data, new(pointer, data.Host)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IterateHost<TData>(
        IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
    {
        host.IterateAllocated(
            new SimpleIterationData<TData>(host, data, handler),
            static (in SimpleIterationData<TData> data, long pointer) =>
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

    public void ForEach(EntityHandler handler)
    {
        var hosts = _hosts.UnsafeRawValues;
        for (int i = 0; i != hosts.Count; ++i) {
            var host = hosts[i];
            if (host.Count != 0) {
                IterateHost(host, handler);
            }
        }
    }

    public void ForEach(SimpleEntityHandler handler)
    {
        var hosts = _hosts.UnsafeRawValues;
        for (int i = 0; i != hosts.Count; ++i) {
            var host = hosts[i];
            if (host.Count != 0) {
                IterateHost(host, handler);
            }
        }
    }

    public void ForEach<TData>(in TData data, EntityHandler<TData> handler)
    {
        var hosts = _hosts.UnsafeRawValues;
        for (int i = 0; i != hosts.Count; ++i) {
            var host = hosts[i];
            if (host.Count != 0) {
                IterateHost(host, data, handler);
            }
        }
    }

    public void ForEach<TData>(in TData data, SimpleEntityHandler<TData> handler)
    {
        var hosts = _hosts.UnsafeRawValues;
        for (int i = 0; i != hosts.Count; ++i) {
            var host = hosts[i];
            if (host.Count != 0) {
                IterateHost(host, data, handler);
            }
        }
    }

    public void Query<TTypeUnion>(EntityHandler handler)
        where TTypeUnion : ITypeUnion, new()
        => Query(Matchers.From<TTypeUnion>(), handler);

    public void Query<TTypeUnion>(SimpleEntityHandler handler)
        where TTypeUnion : ITypeUnion, new()
        => Query(Matchers.From<TTypeUnion>(), handler);

    public void Query(IEntityMatcher matcher, EntityHandler handler)
    {
        foreach (var host in _hosts.AsValueSpan()) {
            if (host.Count != 0 && matcher.Match(host.Descriptor)) {
                IterateHost(host, handler);
            }
        }
    }

    public void Query(IEntityMatcher matcher, SimpleEntityHandler handler)
    {
        foreach (var host in _hosts.AsValueSpan()) {
            if (host.Count != 0 && matcher.Match(host.Descriptor)) {
                IterateHost(host, handler);
            }
        }
    }

    public void Query<TData>(IEntityMatcher matcher, in TData data, EntityHandler<TData> handler)
    {
        var hosts = _hosts.UnsafeRawValues;
        for (int i = 0; i != hosts.Count; ++i) {
            var host = hosts[i];
            if (host.Count != 0 && matcher.Match(host.Descriptor)) {
                IterateHost(host, data, handler);
            }
        }
    }

    public void Query<TData>(IEntityMatcher matcher, in TData data, SimpleEntityHandler<TData> handler)
    {
        var hosts = _hosts.UnsafeRawValues;
        for (int i = 0; i != hosts.Count; ++i) {
            var host = hosts[i];
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
        => GetHost<TEntity, TStorage>(static () => new());

    public WorldEntityHost<TEntity, TStorage> GetHost<TEntity, TStorage>(Func<TStorage> creator)
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

    public WrappedWorldEntityHost<TEntity, THost> GetCustomHost<TEntity, THost>()
        where TEntity : struct
        where THost : IEntityHost<TEntity>, new()
        => GetCustomHost<TEntity, THost>(static () => new());

    public WrappedWorldEntityHost<TEntity, THost> GetCustomHost<TEntity, THost>(Func<THost> creator)
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

    public bool ReleaseHost<THost>()
        where THost : IEntityHost
    {
        if (_hosts.Remove(WorldEntityHostIndexer<THost>.Index, out var host)) {
            OnEntityHostReleased?.Invoke(host);
            return true;
        }
        return false;
    }

    public bool ReleaseHost<THost>([MaybeNullWhen(false)] out IReactiveEntityHost host)
        where THost : IEntityHost
    {
        if (_hosts.Remove(WorldEntityHostIndexer<THost>.Index, out host)) {
            OnEntityHostReleased?.Invoke(host);
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

    public void Send<TEvent>(in EntityRef target, in TEvent e)
        where TEvent : IEvent
        => Dispatcher.Send(target, e);

    public void Modify<TCommand>(in EntityRef target, in TCommand command)
        where TCommand : ICommand
    {
        command.Execute(this, target);
        Dispatcher.Send(target, command);
    }

    public WorldCommandBuffer CreateCommandBuffer()
        => new(this);

    public WorldCommandBuffer<TCommand> CreateCommandBuffer<TCommand>()
        where TCommand : IParallelCommand
        => new(this);

    public TAddon AcquireAddon<TAddon>()
        where TAddon : IAddon, new()
    {
        ref var addon = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _addons, WorldAddonIndexer<TAddon>.Index, out bool exists);

        if (exists) {
            return (TAddon)addon!;
        }

        var newAddon = new TAddon();
        addon = newAddon;
        newAddon.OnInitialize(this);
        return newAddon;
    }

    public TAddon AddAddon<TAddon>()
        where TAddon : IAddon, new()
    {
        ref var addon = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _addons, WorldAddonIndexer<TAddon>.Index, out bool exists);

        if (exists) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }

        var newAddon = new TAddon();
        addon = newAddon;
        newAddon.OnInitialize(this);
        return newAddon;
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
        => _addons.TryGetValue(WorldAddonIndexer<TAddon>.Index, out var addon)
            ? (TAddon)addon : throw new Exception("Addon not found: " + typeof(TAddon));

    public bool ContainsAddon<TAddon>()
        where TAddon : IAddon
        => _addons.ContainsKey(WorldAddonIndexer<TAddon>.Index);
    
    public bool TryGetAddon<TAddon>([MaybeNullWhen(false)] out TAddon addon)
        where TAddon : IAddon
    {
        if (_addons.TryGetValue(WorldAddonIndexer<TAddon>.Index, out var rawAddon)) {
            addon = (TAddon)rawAddon;
            return true;
        }
        addon = default;
        return false;
    }

    private void Dispose(bool disposing)
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