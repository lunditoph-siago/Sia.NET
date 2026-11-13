using System.Runtime.CompilerServices;

namespace Sia;

public partial class World
{
    public sealed class EntityQuery : IReactiveEntityQuery
    {
        public event Action<IReactiveEntityHost>? OnEntityHostAdded;
        public event Action<IReactiveEntityHost>? OnEntityHostRemoved;

        public int Count {
            get {
                var count = 0;
                foreach (var host in _hosts) {
                    count += host.Count;
                }
                return count;
            }
        }

        public int Version { get; private set; }

        public World World { get; private set; }
        public IEntityMatcher Matcher { get; private set; }
        public IReadOnlyList<IReactiveEntityHost> Hosts => _hosts;

        IReadOnlyList<IEntityHost> IEntityQuery.Hosts => _hosts;

        private readonly List<IReactiveEntityHost> _hosts = [];

        internal EntityQuery(World world, IEntityMatcher matcher)
        {
            World = world;
            Matcher = matcher;

            var version = world.Version;
            foreach (var host in world.Hosts) {
                if (matcher.Match(host)) {
                    _hosts.Add(host);
                }
                if (world.Version != version || world.IsDisposed)
                    throw new InvalidOperationException("The world's hosts changed while creating a query.");
            }
        }

        internal void Subscribe()
        {
            World.OnEntityHostAdded += OnEntityHostCreated;
            World.OnEntityHostRemoved += OnEntityHostReleased;
        }

        private void OnEntityHostCreated(IReactiveEntityHost host)
        {
            if (Matcher?.Match(host) == true && World != null) {
                _hosts.Add(host);
                Version++;
                NotifyObservers(OnEntityHostAdded, host, Outcome<Exception>.Success).ThrowIfFailed();
            }
        }

        private void OnEntityHostReleased(IReactiveEntityHost host)
        {
            if (_hosts.Remove(host)) {
                Version++;
                NotifyObservers(OnEntityHostRemoved, host, Outcome<Exception>.Success).ThrowIfFailed();
            }
        }

        public void Dispose()
        {
            if (World == null) {
                return;
            }
            Version++;

            World._queries.Remove(Matcher);
            World.OnEntityHostAdded -= OnEntityHostCreated;
            World.OnEntityHostRemoved -= OnEntityHostReleased;

            _hosts.Clear();
            OnEntityHostAdded = null;
            OnEntityHostRemoved = null;
            World = null!;
            Matcher = null!;
        }
    }

    private sealed class AnyQuery(World world) : IReactiveEntityQuery
    {
        public event Action<IReactiveEntityHost>? OnEntityHostAdded {
            add => world.OnEntityHostAdded += value;
            remove => world.OnEntityHostAdded -= value;
        }

        public event Action<IReactiveEntityHost>? OnEntityHostRemoved {
            add => world.OnEntityHostRemoved += value;
            remove => world.OnEntityHostRemoved -= value;
        }

        public int Count {
            get {
                var count = 0;
                var hosts = world.Hosts;
                for (var i = 0; i < hosts.Count; i++) {
                    count += hosts[i].Count;
                }
                return count;
            }
        }

        public int Version => world.Version;
        public IReadOnlyList<IReactiveEntityHost> Hosts => world.Hosts;
        IReadOnlyList<IEntityHost> IEntityQuery.Hosts => world.Hosts;

        public void Dispose() {}
    }

    public IReadOnlyDictionary<IEntityMatcher, EntityQuery> Queries => _queries;

    private AnyQuery? _anyQuery;
    internal readonly Dictionary<IEntityMatcher, EntityQuery> _queries = [];
    private readonly HashSet<IEntityMatcher> _initializingQueries = [];

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
        for (var i = 0; i != hosts.Count; ++i) {
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
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(matcher);
        if (matcher == Matchers.Any) {
            return _anyQuery ??= new(this);
        }
        if (_queries.TryGetValue(matcher, out var query)) {
            return query;
        }
        if (!_initializingQueries.Add(matcher))
            throw new InvalidOperationException("The same matcher is already creating a query.");
        try {
            query = new(this, matcher);
            _queries.Add(matcher, query);
            query.Subscribe();
            return query;
        }
        finally {
            _initializingQueries.Remove(matcher);
        }
    }
}
