namespace Sia;

using System.Collections.Concurrent;

public class SystemGlobalData
{
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
        if (components == null || components.ProxyTypes.Length == 0) {
            var task = scheduler.CreateTask(dependedTasksResult);
            DoRegisterSystem(sysData, task);

            var children = TSystem.Children;
            var childrenHandles = children != null ? RegisterChildren(children, task) : null;

            return new SystemHandle(
                system, task,
                handle => {
                    scheduler.RemoveTask(handle.TaskGraphNode);
                    if (childrenHandles != null) {
                        foreach (var childHandle in childrenHandles) {
                            childHandle.Dispose();
                        }
                    }
                });
        }

        Func<bool> taskFunc;

        var triggers = TSystem.Triggers;
        if (triggers != null) {
            taskFunc = () => {
                
            }
        }
    }
}

public abstract class SystemBase
{
    public virtual ITypeUnion? Children { get; }
    public virtual ITypeUnion? Dependencies { get; }

    public virtual Scheduler.TaskGraphNode Register(
        World world, Scheduler scheduler, Scheduler.TaskGraphNode? parentTask = null)
    {

        var dependedTasks = new List<Scheduler.TaskGraphNode>();
        if (parentTask != null) {
            dependedTasks.Add(parentTask);
        }
    }
}

public abstract class ExecutableSystemBase : SystemBase
{
    public virtual IEnumerable<ICommand>? Triggers { get; }

    public virtual void BeforeExecute(World world, Scheduler scheduler) {}
    public virtual void Execute(World world, Scheduler scheduler, EntityRef entity) {}
}

public abstract class System<T1> : SystemBase
{
}