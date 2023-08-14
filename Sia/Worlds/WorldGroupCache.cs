namespace Sia;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

public sealed class WorldGroupCache
{
    public sealed class Handle : IDisposable
    {
        public (World<EntityRef>, ITypeUnion) Key { get; private init; }
        public WorldGroupCache Cache { get; private init; }

        public WorldGroup<EntityRef> Group => Cache.Group;
        public int RefCount => Cache.RefCount;

        private bool _disposed;

        internal Handle((World<EntityRef>, ITypeUnion) key, WorldGroupCache cache)
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

    private class WorldGroupKeyComparer : EqualityComparer<(World<EntityRef>, ITypeUnion)>
    {
        public override bool Equals((World<EntityRef>, ITypeUnion) x, (World<EntityRef>, ITypeUnion) y)
            => x.Item1 == y.Item1 && TypeUnionComparer.Instance.Equals(x.Item2, y.Item2);

        public override int GetHashCode([DisallowNull] (World<EntityRef>, ITypeUnion) obj)
            => obj.GetHashCode();
    }

    public static Handle Acquire<TTypeUnion>(World<EntityRef> world)
        where TTypeUnion : ITypeUnion, new()
        => Acquire(world, new TTypeUnion());

    public static Handle Acquire(World<EntityRef> world, ITypeUnion typeUnion)
    {
        var cacheKey = (world, typeUnion);

        if (!s_entries.TryGetValue(cacheKey, out var entry)) {
            entry = s_entries.GetOrAdd(cacheKey, key => new(
                world.CreateGroup(entity => {
                    foreach (var compType in typeUnion.ProxyTypes.AsSpan()) {
                        if (!entity.Contains(compType)) {
                            return false;
                        }
                    }
                    return true;
                }))
            );
        }

        entry.RefCount++;
        return new(cacheKey, entry);
    }

    private static readonly ConcurrentDictionary<(World<EntityRef>, ITypeUnion), WorldGroupCache> s_entries
        = new(new WorldGroupKeyComparer());

    public WorldGroup<EntityRef> Group { get; }
    public int RefCount { get; private set; }

    private WorldGroupCache(WorldGroup<EntityRef> group)
    {
        Group = group;
    }
}