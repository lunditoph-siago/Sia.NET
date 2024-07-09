namespace Sia;

using System.Runtime.CompilerServices;

public partial class World
{
    public sealed class EntityQuery : IReactiveEntityQuery
    {
        public event Action<IReactiveEntityHost>? OnEntityHostAdded;
        public event Action<IReactiveEntityHost>? OnEntityHostRemoved;

        public int Count {
            get {
                int count = 0;
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

            World.OnEntityHostAdded += OnEntityHostCreated;
            World.OnEntityHostRemoved += OnEntityHostReleased;

            foreach (var host in world.Hosts) {
                if (matcher.Match(host)) {
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
            if (Matcher.Match(host)) {
                _hosts.Add(host);
                Version++;
                OnEntityHostAdded?.Invoke(host);
            }
        }

        private void OnEntityHostReleased(IReactiveEntityHost host)
        {
            if (Matcher.Match(host)) {
                _hosts.Remove(host);
                Version++;
                OnEntityHostRemoved?.Invoke(host);
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
            Version++;

            World._queries.Remove(Matcher);
            World.OnEntityHostAdded -= OnEntityHostCreated;
            World.OnEntityHostRemoved -= OnEntityHostReleased;

            World = null!;
            Matcher = null!;
        }
    }

    public IReadOnlyDictionary<IEntityMatcher, EntityQuery> Queries => _queries;

    internal readonly Dictionary<IEntityMatcher, EntityQuery> _queries = [];

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
}