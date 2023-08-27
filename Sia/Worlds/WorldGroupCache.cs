namespace Sia;

public class WorldGroupCacheLibrary
{
    public IReadOnlyDictionary<IMatcher, WorldGroupCache> Caches => UnsafeCaches;
    internal Dictionary<IMatcher, WorldGroupCache> UnsafeCaches = new();

    public void Clear()
    {
        foreach (var cache in UnsafeCaches.Values) {
            cache.RefCount = 0;
            cache.Group.Clear();
        }
        UnsafeCaches.Clear();
    }
}

public sealed class WorldGroupCache
{
    public class Handle : IDisposable
    {
        public required World<EntityRef> World { get; init; }
        public required IMatcher Matcher { get; init; }
        public required WorldGroupCache Cache { get; init; }

        public WorldGroup<EntityRef> Group => Cache.Group;
        public int RefCount => Cache.RefCount;

        private bool _disposed;

        internal Handle() {}

        private void DoDispose()
        {
            if (_disposed || Cache.RefCount == 0) { return; }

            Cache.RefCount--;
            if (Cache.RefCount == 0) {
                var entries = World.GetAddon<WorldGroupCacheLibrary>().UnsafeCaches;
                entries.Remove(Matcher);
                Group.Clear();
            }

            _disposed = true;
        }

        ~Handle()
        {
            DoDispose();
        }

        public void Dispose()
        {
            DoDispose();
            GC.SuppressFinalize(this);
        }
    }

    public static Handle Acquire<TTypeUnion>(World<EntityRef> world)
        where TTypeUnion : ITypeUnion, new()
        => Acquire(world, new TTypeUnion().ToMatcher());

    public static Handle Acquire(World<EntityRef> world, IMatcher matcher)
    {
        var entries = world.AcquireAddon<WorldGroupCacheLibrary>().UnsafeCaches;
        if (!entries.TryGetValue(matcher, out var cache)) {
            cache = new(world.CreateGroup(matcher.Match));
        }
        cache.RefCount++;
        return new Handle {
            World = world,
            Matcher = matcher,
            Cache = cache
        };
    }

    public WorldGroup<EntityRef> Group { get; }
    public int RefCount { get; internal set; }

    private WorldGroupCache(WorldGroup<EntityRef> group)
    {
        Group = group;
    }
}

public static class WorldGroupCacheExtensions
{
    public static WorldGroupCache.Handle Query(this World<EntityRef> world, IMatcher matcher)
        => WorldGroupCache.Acquire(world, matcher);
}