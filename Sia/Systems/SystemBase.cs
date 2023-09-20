namespace Sia;

public class SystemBase<TWorld> : ISystem
    where TWorld : World
{
    public ISystemUnion? Children { get; init; }
    public ISystemUnion? Dependencies { get; init; }
    public IEntityMatcher? Matcher { get; init; }
    public IEventUnion? Trigger { get; init; }
    public IEventUnion? Filter { get; init; }

    public virtual void Initialize(TWorld world, Scheduler scheduler) {}
    public virtual void Uninitialize(TWorld world, Scheduler scheduler) {}

    public virtual void BeforeExecute(TWorld world, Scheduler scheduler) {}
    public virtual void AfterExecute(TWorld world, Scheduler scheduler) {}
    public virtual void Execute(TWorld world, Scheduler scheduler, in EntityRef entity) {}

    public virtual bool OnTriggerEvent<TEvent>(TWorld world, Scheduler scheduler, in EntityRef entity, in TEvent e)
        where TEvent : IEvent
        => true;

    public virtual bool OnFilterEvent<TEvent>(TWorld world, Scheduler scheduler, in EntityRef entity, in TEvent e)
        where TEvent : IEvent
        => true;

    private record MatchAnyEventListener(
        SystemBase<TWorld> System, TWorld World, Scheduler Scheduler, Group Group,
        HashSet<Type> TriggerTypes, bool HasRemoveTrigger) : IEventListener
    {
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

    private record MatchAnyFilterableEventListener(
        SystemBase<TWorld> System, TWorld World, Scheduler Scheduler, Group<EntityRef> Group,
        HashSet<Type> TriggerTypes, HashSet<Type> FilterTypes, bool HasRemoveTrigger) : IEventListener
    {
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

    private record TargetEventListener(
        SystemBase<TWorld> System, TWorld World, Scheduler Scheduler, Group<EntityRef> Group,
        Dictionary<EntityRef, IEventListener> Listeners,
        HashSet<Type> TriggerTypes, bool HasRemoveTrigger) : IEventListener
    {
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

    private record TargetFilterableEventListener(
        SystemBase<TWorld> System, TWorld World, Scheduler Scheduler, Group Group,
        HashSet<EntityRef> ListeningEntities, HashSet<Type> TriggerTypes, HashSet<Type> FilterTypes,
        bool HasRemoveTrigger) : IEventListener
    {
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

    SystemHandle ISystem.Register(
        World world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks)
        => Register((TWorld)world, scheduler, dependedTasks);

    public SystemHandle Register(
        TWorld world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null)
    {
        var systemLib = world.AcquireAddon<SystemLibrary>();

        void AddDependedSystemTasks(ISystemUnion systemTypes, List<Scheduler.TaskGraphNode> result)
        {
            foreach (var systemType in systemTypes.ProxyTypes) {
                var sysEntry = systemLib.Acquire(systemType);
                if (!sysEntry._taskGraphNodes.TryGetValue(scheduler, out var taskNode)) {
                    throw new InvalidSystemDependencyException(
                        $"Failed to register system: Depended system type '{this}' is not registered.");
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
                var childSysType = types[i];
                var childSysEntry = systemLib.Acquire(childSysType);
                var childSysCreator = SystemLibrary.GetCreator(childSysType);
                handles[i] = childSysCreator().Register(world, scheduler, childDependedTasks);
            }

            return handles;
        }

        void DoRegisterSystem(SystemLibrary.Entry sysEntry, Scheduler.TaskGraphNode task)
        {
            if (!sysEntry._taskGraphNodes.TryAdd(scheduler, task)) {
                throw new SystemAlreadyRegisteredException(
                    "Failed to register system: system already registered in World and Scheduler pair");
            }
            Initialize(world, scheduler);
        }

        void DoUnregisterSystem(SystemLibrary.Entry sysEntry, Scheduler.TaskGraphNode task)
        {
            if (!sysEntry._taskGraphNodes.Remove(scheduler, out var removedTask)) {
                throw new ObjectDisposedException("System has been disposed");
            }
            if (removedTask != task) {
                throw new InvalidOperationException("Internal error: removed task is not the task to be disposed");
            }
            Uninitialize(world, scheduler);
        }

        var sysEntry = systemLib.Acquire(GetType());

        var dependedTasksResult = new List<Scheduler.TaskGraphNode>();
        if (dependedTasks != null) {
            dependedTasksResult.AddRange(dependedTasks);
        }

        if (Dependencies != null) {
            AddDependedSystemTasks(Dependencies, dependedTasksResult);
        }

        Scheduler.TaskGraphNode? task;
        SystemHandle[]? childrenHandles;

        var matcher = Matcher;
        if (matcher == null || matcher == Matchers.None) {
            task = scheduler.CreateTask(dependedTasksResult);
            task.UserData = this;

            DoRegisterSystem(sysEntry, task);
            childrenHandles = Children != null ? RegisterChildren(Children, task) : null;

            return new SystemHandle(
                this, task,
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

        var trigger = Trigger;
        var filter = Filter;
        var dispatcher = world.Dispatcher;

        if (trigger == null && filter == null) {
            IEntityQuery query;

            if (matcher == Matchers.Any) {
                query = world;
                disposeFunc = () => {};
            }
            else {
                query = world.Query(matcher);
                disposeFunc = query.Dispose;
            }

            void EntityHandler(in EntityRef entity)
                => Execute(world, scheduler, entity);

            taskFunc = () => {
                BeforeExecute(world, scheduler);
                query.ForEach(EntityHandler);
                AfterExecute(world, scheduler);
                return false;
            };
        }
        else {
            var group = new Group();

            taskFunc = () => {
                BeforeExecute(world, scheduler);
                int count = group.Count;
                if (count != 0) {
                    for (int i = 0; i < count; ++i) {
                        Execute(world, scheduler, group[i]);
                        count = group.Count;
                    }
                    group.Clear();
                }
                AfterExecute(world, scheduler);
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
                    var listener = new MatchAnyFilterableEventListener(
                        System: this,
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

                    var listener = new TargetFilterableEventListener(
                        System: this,
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
                            if (OnTriggerEvent(world, scheduler, target, e)) {
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
                    var listener = new MatchAnyEventListener(
                        System: this,
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

                    var listener = new TargetEventListener(
                        System: this,
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
                                if (OnTriggerEvent(world, scheduler, target, e)) {
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
        childrenHandles = Children != null ? RegisterChildren(Children, task) : null;

        SystemHandle? handle = null;

        void OnWorldDisposed(World world) => handle!.Dispose();
        world.OnDisposed += OnWorldDisposed;

        handle = new SystemHandle(
            this, task,
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

public class SystemBase : SystemBase<World>
{
}