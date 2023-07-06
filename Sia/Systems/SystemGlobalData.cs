namespace Sia;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

public class SystemGlobalData
{
    private class WorldGroupKeyComparer : EqualityComparer<(World<EntityRef>, ITypeUnion)>
    {
        public override bool Equals((World<EntityRef>, ITypeUnion) x, (World<EntityRef>, ITypeUnion) y)
            => x.Item1 == y.Item1 && TypeUnionComparer.Instance.Equals(x.Item2, y.Item2);

        public override int GetHashCode([DisallowNull] (World<EntityRef>, ITypeUnion) obj)
            => obj.GetHashCode();
    }

    internal class WorldGroupCacheEntry
    {
        public WorldGroup<EntityRef> Group { get; }
        public int RefCount { get; set; }

        public WorldGroupCacheEntry(WorldGroup<EntityRef> group)
        {
            Group = group;
        }
    }

    internal static ConcurrentDictionary<(World<EntityRef>, ITypeUnion), WorldGroupCacheEntry> WorldGroupCache { get; }
        = new(new WorldGroupKeyComparer());

    public static SystemGlobalData? Get(Type systemType)
        => s_instances.TryGetValue(systemType, out var instance) ? instance : null;

    internal static SystemGlobalData Acquire(Type systemType)
    {
        if (!s_instances.TryGetValue(systemType, out var instance)) {
            instance = s_instances.GetOrAdd(systemType, type => new());
        }
        return instance;
    }

    internal static SystemGlobalData Acquire<TSystem>()
        where TSystem : ISystem, new()
    {
        var data = Acquire(typeof(TSystem));
        data.Creator ??= () => new TSystem();
        return data;
    }

    private static readonly ConcurrentDictionary<Type, SystemGlobalData> s_instances = new();

    private SystemGlobalData() {}

    public Func<ISystem>? Creator;

    internal ConcurrentDictionary<(World<EntityRef>, Scheduler), Scheduler.TaskGraphNode> RegisterEntries { get; } = new();
}