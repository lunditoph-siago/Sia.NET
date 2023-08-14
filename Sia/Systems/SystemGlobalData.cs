namespace Sia;

using System.Collections.Concurrent;

public class SystemGlobalData
{
    public static SystemGlobalData? Get<TSystem>()
        where TSystem : ISystem
        => Get(typeof(TSystem));

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

    public Func<ISystem>? Creator { get; private set; }

    internal ConcurrentDictionary<(World<EntityRef>, Scheduler), Scheduler.TaskGraphNode> RegisterEntries { get; } = new();
}