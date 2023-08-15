namespace Sia;

using System.Collections.Concurrent;

public sealed class WorldGroupCache
{
    public sealed class Handle : IDisposable
    {
        public (World<EntityRef>, IMatcher) Key { get; private init; }
        public WorldGroupCache Cache { get; private init; }

        public WorldGroup<EntityRef> Group => Cache.Group;
        public int RefCount => Cache.RefCount;

        private bool _disposed;

        internal Handle((World<EntityRef>, IMatcher) key, WorldGroupCache cache)
        {
            Key = key;
            Cache = cache;
        }

        private void DoDispose()
        {
            if (_disposed) { return; }

            Cache.RefCount--;
            if (Cache.RefCount == 0) {
                s_entries.TryRemove(KeyValuePair.Create(Key, Cache));
                Cache.Group.Clear();
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
        var cacheKey = (world, matcher);

        if (!s_entries.TryGetValue(cacheKey, out var entry)) {
            entry = s_entries.GetOrAdd(cacheKey,
                key => new(world.CreateGroup(matcher.Match)));
        }

        entry.RefCount++;
        return new(cacheKey, entry);
    }

    private static readonly ConcurrentDictionary<(World<EntityRef>, IMatcher), WorldGroupCache> s_entries = new();

    public WorldGroup<EntityRef> Group { get; }
    public int RefCount { get; private set; }

    private WorldGroupCache(WorldGroup<EntityRef> group)
    {
        Group = group;
    }
}