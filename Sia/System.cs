namespace Sia;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

public class SystemGlobalData
{
    private class WorldGroupKeyComparer : EqualityComparer<(World, ITypeUnion)>
    {
        public override bool Equals((World, ITypeUnion) x, (World, ITypeUnion) y)
            => x.Item1 == y.Item1 && TypeUnionComparer.Instance.Equals(x.Item2, y.Item2);

        public override int GetHashCode([DisallowNull] (World, ITypeUnion) obj)
            => obj.GetHashCode();
    }

    internal class WorldGroupCacheEntry
    {
        public Group<EntityRef> Group { get; }
        public int RefCount { get; set; }

        public WorldGroupCacheEntry(Group<EntityRef> group)
        {
            Group = group;
        }
    }

    internal static ConcurrentDictionary<(World, ITypeUnion), WorldGroupCacheEntry> WorldGroupCache { get; }
        = new(new WorldGroupKeyComparer());

    public static SystemGlobalData? Get(Type systemType)
        => s_instances.TryGetValue(systemType, out var instance) ? instance : null;

    internal static SystemGlobalData Acquire<TSystem>()
        where TSystem : ISystem, new()
    {
        var type = typeof(TSystem);
        if (!s_instances.TryGetValue(type, out var instance)) {
            instance = s_instances.GetOrAdd(type, type =>
                new SystemGlobalData {
                    Children = TSystem.Children,
                    Components = TSystem.Components,
                    Triggers = TSystem.Triggers,
                    Creator = () => new TSystem(),
                    Registerer = (system, world, sched, dependedTasks) =>
                        ((TSystem)system).Register(world, sched, dependedTasks),
                });
        }
        return instance;
    }

    private static ConcurrentDictionary<Type, SystemGlobalData> s_instances = new();

    private SystemGlobalData() {}

    public required ITypeUnion? Children { get; init; }
    public ITypeUnion? Components { get; init; }
    public IEnumerable<ICommand>? Triggers { get; init; }

    public required Func<ISystem> Creator;
    public required Func<ISystem, World, Scheduler, Scheduler.TaskGraphNode[]?, SystemHandle> Registerer { get; init; }

    internal ConcurrentDictionary<(World, Scheduler), Scheduler.TaskGraphNode> RegisterEntries { get; } = new();
}

public class SystemHandle : IDisposable
{
    public ISystem System { get; }
    public Scheduler.TaskGraphNode TaskGraphNode { get; }

    private Action<SystemHandle> _onDispose;
    private bool _disposed;

    internal SystemHandle(
        ISystem system, Scheduler.TaskGraphNode taskGraphNode, Action<SystemHandle> onDispose)
    {
        System = system;
        TaskGraphNode = taskGraphNode;
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _onDispose(this);
    }
}

public static class SystemExtensions
{
    public static SystemHandle Register<TSystem>(
        this TSystem system, World world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null)
        where TSystem : ISystem, new()
    {
        void AddDependedSystemTasks(ISystemUnion systemTypes, List<Scheduler.TaskGraphNode> result)
        {
            foreach (var systemType in systemTypes.ProxyTypes) {
                var sysData = SystemGlobalData.Get(systemType);
                if (sysData == null) {
                    throw new ArgumentException(
                        $"Failed to register system: invalid depended system type '{systemType}'");
                }
                if (!sysData.RegisterEntries.TryGetValue((world, scheduler), out var taskNode)) {
                    throw new InvalidSystemDependencyException(
                        $"Failed to register system: Depended system type '{system}' not registered.");
                }
                result.Add(taskNode);
            }
        }

        SystemHandle[] RegisterChildren(
            ITypeUnion children, Scheduler.TaskGraphNode taskNode)
        {
            var types = children.ProxyTypes;
            int count = types.Length;
            var handles = new SystemHandle[count];
            var childDependedTasks = new[] { taskNode };

            for (int i = 0; i != count; ++i) {
                var childSysData = SystemGlobalData.Get(types[i]);
                if (childSysData == null) {
                    for (int j = 0; j != i; ++j) {
                        handles[j].Dispose();
                    }
                    throw new InvalidSystemChildException(
                        $"Failed to register system: invalid child system type '{types[i]}'");
                }
                handles[i] = childSysData.Registerer(
                    childSysData.Creator(), world, scheduler, childDependedTasks);
            }

            return handles;
        }

        void DoRegisterSystem(SystemGlobalData sysData, Scheduler.TaskGraphNode task)
        {
            if (!sysData.RegisterEntries.TryAdd((world, scheduler), task)) {
                throw new SystemAlreadyRegisteredException(
                    "Failed to register system: system already registered in World and Scheduler pair");
            }
        }

        void DoUnregisterSystem(SystemGlobalData sysData, Scheduler.TaskGraphNode task)
        {
            if (!sysData.RegisterEntries.Remove((world, scheduler), out var removedTask)) {
                throw new ObjectDisposedException("System has been disposed");
            }
            if (removedTask != task) {
                throw new ObjectDisposedException("Internal error: removed task is not the task to be disposed");
            }
        }

        var sysData = SystemGlobalData.Acquire<TSystem>();

        var dependedTasksResult = new List<Scheduler.TaskGraphNode>();
        if (dependedTasks != null) {
            dependedTasksResult.AddRange(dependedTasks);
        }

        var dependencies = TSystem.Dependencies;
        if (dependencies != null) {
            AddDependedSystemTasks(dependencies, dependedTasksResult);
        }

        var components = TSystem.Components;
        var children = TSystem.Children;

        Scheduler.TaskGraphNode? task;
        SystemHandle[]? childrenHandles;

        if (components == null || components.ProxyTypes.Length == 0) {
            task = scheduler.CreateTask(dependedTasksResult);

            DoRegisterSystem(sysData, task);
            childrenHandles = children != null ? RegisterChildren(children, task) : null;

            return new SystemHandle(
                system, task,
                handle => {
                    DoUnregisterSystem(sysData, task);
                    scheduler.RemoveTask(handle.TaskGraphNode);

                    if (childrenHandles != null) {
                        foreach (var childHandle in childrenHandles) {
                            childHandle.Dispose();
                        }
                    }
                });
        }

        var compTypes = components.ProxyTypes;

        Func<bool> taskFunc;
        Action disposeFunc;

        var triggers = TSystem.Triggers;
        if (triggers != null) {
            var dispatcher = world.Dispatcher;
            var group = new Group<EntityRef>();
            var entityListeners = new Dictionary<EntityRef, Dispatcher.Listener>();

            var triggerSet = new HashSet<ICommand>(triggers);
            bool hasAddTrigger = triggerSet.Contains(WorldCommands.Add.Instance);
            bool hasRemoveTrigger = triggerSet.Contains(WorldCommands.Remove.Instance);

            Dispatcher.Listener entityAddListener = (EntityRef target, ICommand command) => {
                foreach (var compType in compTypes.AsSpan()) {
                    if (!target.Contains(compType)) {
                        return false;
                    }
                }

                Dispatcher.Listener commandListener = (EntityRef target, ICommand command) => {
                    if (triggerSet.Contains(command)) {
                        group.Add(target);
                    }
                    if (command is WorldCommands.Add) {
                        entityListeners.Remove(target);
                        if (hasRemoveTrigger) {
                            group.Add(target);
                        }
                        else {
                            group.Remove(target);
                        }
                        return true;
                    }
                    return false;
                };

                dispatcher.Listen(target, commandListener);
                entityListeners.Add(target, commandListener);

                if (hasAddTrigger) {
                    group.Add(target);
                }
                return false;
            };

            dispatcher.Listen<WorldCommands.Add>(entityAddListener);

            taskFunc = () => {
                int count = group.Count;
                if (count == 0) {
                    return false;
                }
                system.BeforeExecute(world, scheduler);
                for (int i = 0; i < count; ++i) {
                    system.Execute(world, scheduler, group[i]);
                    count = group.Count;
                }
                group.Clear();
                return false;
            };

            disposeFunc = () => {
                dispatcher.Unlisten<WorldCommands.Add>(entityAddListener);
                foreach (var (entity, listener) in entityListeners) {
                    dispatcher.Unlisten(entity, listener);
                }
            };
        }
        else {
            var worldGroupCache = SystemGlobalData.WorldGroupCache;
            var cacheKey = (world, components);

            if (!worldGroupCache.TryGetValue(cacheKey, out var entry)) {
                entry = worldGroupCache.GetOrAdd(cacheKey, key => new(
                    world.CreateGroup(entity => {
                        foreach (var compType in compTypes.AsSpan()) {
                            if (!entity.Contains(compType)) {
                                return false;
                            }
                        }
                        return true;
                    }))
                );
                entry.RefCount++;
            }

            var group = entry.Group;
            taskFunc = () => {
                var span = group.AsSpan();
                if (span.Length == 0) {
                    return false;
                }
                system.BeforeExecute(world, scheduler);
                foreach (var entity in span) {
                    system.Execute(world, scheduler, entity);
                }
                return false;
            };

            disposeFunc = () => {
                entry.RefCount--;
                if (entry.RefCount == 0) {
                    if(!worldGroupCache.TryRemove(KeyValuePair.Create(cacheKey, entry))) {
                        throw new ObjectDisposedException("Failed to remove cached system group");
                    }
                }
            };
        }

        task = scheduler.CreateTask(taskFunc, dependedTasksResult);

        DoRegisterSystem(sysData, task);
        childrenHandles = children != null ? RegisterChildren(children, task) : null;

        return new SystemHandle(
            system, task,
            handle => {
                DoUnregisterSystem(sysData, task);
                scheduler.RemoveTask(handle.TaskGraphNode);
                disposeFunc();

                if (childrenHandles != null) {
                    foreach (var childHandle in childrenHandles) {
                        childHandle.Dispose();
                    }
                }
            });
    }
}