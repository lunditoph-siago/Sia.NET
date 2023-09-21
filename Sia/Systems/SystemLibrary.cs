namespace Sia;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

public class SystemLibrary : IAddon
{
    public class Entry
    {
        public IReadOnlyDictionary<Scheduler, Scheduler.TaskGraphNode> TaskGraphNodes => _taskGraphNodes;
        internal Dictionary<Scheduler, Scheduler.TaskGraphNode> _taskGraphNodes = new();
    }

    private readonly record struct MatchAnyEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, Group Group,
        HashSet<Type> TriggerTypes, bool HasRemoveTrigger) : IEventListener
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                if (HasRemoveTrigger) {
                    if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                        Group.Add(target);
                    }
                }
                else {
                    Group.Remove(target);
                }
            }
            else if (TriggerTypes.Contains(e.GetType())) {
                if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                    Group.Add(target);
                }
            }
            return false;
        }
    }

    private readonly record struct MatchAnyFilterableEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, Group<EntityRef> Group,
        HashSet<Type> TriggerTypes, HashSet<Type> FilterTypes, bool HasRemoveTrigger) : IEventListener
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                if (HasRemoveTrigger) {
                    if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                        Group.Add(target);
                    }
                }
                else {
                    Group.Remove(target);
                }
                return false;
            }
            if (TriggerTypes.Contains(type)) {
                if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                    Group.Add(target);
                }
            }
            else if (FilterTypes.Contains(type)) {
                if (System.OnFilterEvent(World, Scheduler, target, e)) {
                    Group.Remove(target);
                }
            }
            return false;
        }
    }

    private readonly record struct TargetEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, Group<EntityRef> Group,
        Dictionary<EntityRef, IEventListener> Listeners,
        HashSet<Type> TriggerTypes, bool HasRemoveTrigger) : IEventListener
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Listeners.Remove(target);
                if (HasRemoveTrigger) {
                    if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                        Group.Add(target);
                    }
                }
                else {
                    Group.Remove(target);
                }
                return true;
            }
            if (TriggerTypes.Contains(e.GetType())) {
                if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                    Group.Add(target);
                }
            }
            return false;
        }
    }

    private readonly record struct TargetFilterableEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, Group Group,
        HashSet<EntityRef> ListeningEntities, HashSet<Type> TriggerTypes, HashSet<Type> FilterTypes,
        bool HasRemoveTrigger) : IEventListener
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                ListeningEntities.Remove(target);
                if (HasRemoveTrigger) {
                    if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                        Group.Add(target);
                    }
                }
                else {
                    Group.Remove(target);
                }
                return true;
            }
            if (TriggerTypes.Contains(type)) {
                if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                    Group.Add(target);
                }
            }
            else if (FilterTypes.Contains(type)) {
                if (System.OnFilterEvent(World, Scheduler, target, e)) {
                    Group.Remove(target);
                }
            }
            return false;
        }
    }

    public delegate SystemHandle SystemRegisterer(
        SystemLibrary lib, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null);

    private World? _world;
    private readonly Dictionary<Type, Entry> _systemEntries = new();

    private readonly static ConcurrentDictionary<Type, SystemRegisterer> s_systemRegisterers = new();

    public void OnInitialize(World world)
    {
        _world = world;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Ensure<TSystem>()
        where TSystem : ISystem, new()
        => s_systemRegisterers.TryAdd(typeof(TSystem),
            static (lib, scheduler, dependedTasks) => lib.Register<TSystem>(scheduler, dependedTasks));

    public static SystemRegisterer GetRegisterer(Type systemType)
        => s_systemRegisterers[systemType];

    public static SystemRegisterer GetRegisterer<TSystem>()
        where TSystem : ISystem
        => s_systemRegisterers[typeof(TSystem)];

    public Entry Get<TSystem>() where TSystem : ISystem
        => Get(typeof(TSystem));

    public Entry Get(Type systemType)
        => _systemEntries[systemType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Entry Acquire(Type systemType)
    {
        if (!_systemEntries.TryGetValue(systemType, out var instance)) {
            instance = new();
            _systemEntries.Add(systemType, instance);
        }
        return instance;
    }

    public SystemHandle Register<TSystem>(Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null)
        where TSystem : ISystem, new()
    {
        var system = new TSystem();
        var world = _world!;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddDependedSystemTasks(ISystemUnion systemTypes, List<Scheduler.TaskGraphNode> result)
        {
            foreach (var systemType in systemTypes.ProxyTypes) {
                var sysEntry = Acquire(systemType);
                if (!sysEntry._taskGraphNodes.TryGetValue(scheduler, out var taskNode)) {
                    throw new InvalidSystemDependencyException(
                        $"Failed to register system: Depended system type '{this}' is not registered.");
                }
                result.Add(taskNode);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        SystemHandle[] RegisterChildren(
            ITypeUnion children, Scheduler.TaskGraphNode taskNode)
        {
            var types = children.ProxyTypes;
            int count = types.Length;
            var handles = new SystemHandle[count];
            var childDependedTasks = new[] { taskNode };

            for (int i = 0; i != count; ++i) {
                var childSysType = types[i];
                handles[i] = GetRegisterer(childSysType)(this, scheduler, childDependedTasks);
            }

            return handles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DoRegisterSystem(Entry sysEntry, Scheduler.TaskGraphNode task)
        {
            if (!sysEntry._taskGraphNodes.TryAdd(scheduler, task)) {
                throw new SystemAlreadyRegisteredException(
                    "Failed to register system: system already registered in World and Scheduler pair");
            }
            system.Initialize(world, scheduler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DoUnregisterSystem(Entry sysEntry, Scheduler.TaskGraphNode task)
        {
            if (!sysEntry._taskGraphNodes.Remove(scheduler, out var removedTask)) {
                throw new ObjectDisposedException("System has been disposed");
            }
            if (removedTask != task) {
                throw new InvalidOperationException("Internal error: removed task is not the task to be disposed");
            }
            system.Uninitialize(world, scheduler);
        }

        var sysEntry = Acquire(system.GetType());

        var dependedTasksResult = new List<Scheduler.TaskGraphNode>();
        if (dependedTasks != null) {
            dependedTasksResult.AddRange(dependedTasks);
        }

        if (system.Dependencies != null) {
            AddDependedSystemTasks(system.Dependencies, dependedTasksResult);
        }

        Scheduler.TaskGraphNode? task;
        SystemHandle[]? childrenHandles;

        var matcher = system.Matcher;
        var children = system.Children;

        if (matcher == null || matcher == Matchers.None) {
            task = scheduler.CreateTask(dependedTasksResult);
            task.UserData = this;

            DoRegisterSystem(sysEntry, task);
            childrenHandles = children != null ? RegisterChildren(children, task) : null;

            return new SystemHandle(
                system, task,
                handle => {
                    DoUnregisterSystem(sysEntry, task);

                    if (childrenHandles != null) {
                        for (int i = childrenHandles.Length - 1; i >= 0; --i) {
                            childrenHandles[i].Dispose();
                        }
                    }
                    scheduler.RemoveTask(handle.Task);
                });
        }

        Func<bool> taskFunc;
        Action disposeFunc;

        var trigger = system.Trigger;
        var filter = system.Filter;
        var dispatcher = world.Dispatcher;

        if (trigger == null && filter == null) {
            void EntityHandler(in EntityRef entity)
                => system.Execute(world, scheduler, entity);

            if (matcher == Matchers.Any) {
                taskFunc = () => {
                    system.BeforeExecute(world, scheduler);
                    world.ForEach(EntityHandler);
                    system.AfterExecute(world, scheduler);
                    return false;
                };
                disposeFunc = () => {};
            }
            else {
                var query = world.Query(matcher);
                taskFunc = () => {
                    system.BeforeExecute(world, scheduler);
                    query.ForEach(EntityHandler);
                    system.AfterExecute(world, scheduler);
                    return false;
                };
                disposeFunc = query.Dispose;
            }
        }
        else {
            var group = new Group();

            taskFunc = () => {
                system.BeforeExecute(world, scheduler);
                int count = group.Count;
                if (count != 0) {
                    for (int i = 0; i < count; ++i) {
                        system.Execute(world, scheduler, group[i]);
                        count = group.Count;
                    }
                    group.Clear();
                }
                system.AfterExecute(world, scheduler);
                return false;
            };

            if (trigger != null && filter != null) {
                var triggerTypes = new HashSet<Type>(trigger.ProxyTypes);
                var filterTypes = new HashSet<Type>(filter.ProxyTypes);

                foreach (var filterType in filterTypes) {
                    triggerTypes.Remove(filterType);
                }
                bool hasRemoveTrigger = triggerTypes.Contains(typeof(WorldEvents.Remove));

                if (matcher == Matchers.Any) {
                    var listener = new MatchAnyFilterableEventListener<TSystem>(
                        System: system,
                        World: world,
                        Scheduler: scheduler,
                        Group: group,
                        TriggerTypes: triggerTypes,
                        FilterTypes: filterTypes,
                        HasRemoveTrigger: hasRemoveTrigger);

                    dispatcher.Listen(listener);
                    disposeFunc = () => dispatcher.Unlisten(listener);
                }
                else {
                    var listeningEntities = new HashSet<EntityRef>();
                    bool hasAddTrigger = triggerTypes.Contains(typeof(WorldEvents.Add));

                    var listener = new TargetFilterableEventListener<TSystem>(
                        System: system,
                        World: world,
                        Scheduler: scheduler,
                        Group: group,
                        ListeningEntities: listeningEntities,
                        TriggerTypes: triggerTypes,
                        FilterTypes: filterTypes,
                        HasRemoveTrigger: hasRemoveTrigger);

                    bool OnEntityAdded(in EntityRef target, in WorldEvents.Add e)
                    {
                        if (!matcher.Match(target.Host.Descriptor)) {
                            return false;
                        }

                        dispatcher.Listen(target, listener);
                        listeningEntities.Add(target);

                        if (hasAddTrigger) {
                            if (system.OnTriggerEvent(world, scheduler, target, e)) {
                                group.Add(target);
                            }
                        }
                        return false;
                    }

                    dispatcher.Listen<WorldEvents.Add>(OnEntityAdded);

                    disposeFunc = () => {
                        dispatcher.Unlisten<WorldEvents.Add>(OnEntityAdded);
                        foreach (var entity in listeningEntities) {
                            dispatcher.Unlisten(entity, listener);
                        }
                    };
                }
            }
            else if (trigger != null) {
                var triggerTypes = new HashSet<Type>(trigger.ProxyTypes);
                bool hasRemoveTrigger = triggerTypes.Contains(typeof(WorldEvents.Remove));

                if (matcher == Matchers.Any) {
                    var listener = new MatchAnyEventListener<TSystem>(
                        System: system,
                        World: world,
                        Scheduler: scheduler,
                        Group: group,
                        TriggerTypes: triggerTypes,
                        HasRemoveTrigger: hasRemoveTrigger);

                    dispatcher.Listen(listener);
                    disposeFunc = () => dispatcher.Unlisten(listener);
                }
                else {
                    var listeners = new Dictionary<EntityRef, IEventListener>();
                    bool hasAddTrigger = triggerTypes.Contains(typeof(WorldEvents.Add));

                    var listener = new TargetEventListener<TSystem>(
                        System: system,
                        World: world,
                        Scheduler: scheduler,
                        Group: group,
                        Listeners: listeners,
                        TriggerTypes: triggerTypes,
                        HasRemoveTrigger: hasRemoveTrigger);

                    bool OnEntityAdded(in EntityRef target, in WorldEvents.Add e)
                    {
                        if (matcher.Match(target.Host.Descriptor)) {
                            dispatcher.Listen(target, listener);
                            listeners.Add(target, listener);

                            if (hasAddTrigger) {
                                if (system.OnTriggerEvent(world, scheduler, target, e)) {
                                    group.Add(target);
                                }
                            }
                        }
                        return false;
                    }

                    dispatcher.Listen<WorldEvents.Add>(OnEntityAdded);

                    disposeFunc = () => {
                        dispatcher.Unlisten<WorldEvents.Add>(OnEntityAdded);
                        foreach (var (entity, listener) in listeners) {
                            dispatcher.Unlisten(entity, listener);
                        }
                    };
                }
            }
            else {
                throw new InvalidSystemAttributeException(
                    "Failed to register system: system must have non-null trigger when filter is specified");
            }
        }

        task = scheduler.CreateTask(taskFunc, dependedTasksResult);
        task.UserData = this;

        DoRegisterSystem(sysEntry, task);
        childrenHandles = children != null ? RegisterChildren(children, task) : null;

        SystemHandle? handle = null;

        void OnWorldDisposed(World world) => handle!.Dispose();
        world.OnDisposed += OnWorldDisposed;

        handle = new SystemHandle(
            system, task,
            handle => {
                world.OnDisposed -= OnWorldDisposed;

                DoUnregisterSystem(sysEntry, task);
                disposeFunc();

                if (childrenHandles != null) {
                    for (int i = childrenHandles.Length - 1; i >= 0; --i) {
                        childrenHandles[i].Dispose();
                    }
                }
                scheduler.RemoveTask(handle.Task);
            });

        return handle;
    }
}