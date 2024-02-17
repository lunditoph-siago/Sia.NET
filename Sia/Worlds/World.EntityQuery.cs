namespace Sia;

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
                OnEntityHostAdded?.Invoke(host);
            }
        }

        private void OnEntityHostReleased(IReactiveEntityHost host)
        {
            if (Matcher.Match(host)) {
                _hosts.Remove(host);
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

            World._queries.Remove(Matcher);
            World.OnEntityHostAdded -= OnEntityHostCreated;
            World.OnEntityHostRemoved -= OnEntityHostReleased;

            World = null!;
            Matcher = null!;
        }
    }
}