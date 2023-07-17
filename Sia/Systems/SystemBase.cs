namespace Sia;

public class SystemBase<TWorld> : ISystem
    where TWorld : World<EntityRef>
{
    public ISystemUnion? Children { get; init; }
    public ISystemUnion? Dependencies { get; init; }
    public ITypeUnion? Matcher { get; init; }
    public IEventUnion? Trigger { get; init; }

    public virtual void Initialize(TWorld world, Scheduler scheduler) {}
    public virtual void Uninitialize(TWorld world, Scheduler scheduler) {}
    public virtual void BeforeExecute(TWorld world, Scheduler scheduler) {}
    public virtual void AfterExecute(TWorld world, Scheduler scheduler) {}
    public virtual void Execute(TWorld world, Scheduler scheduler, in EntityRef entity) {}

    SystemHandle ISystem.Register(
        World<EntityRef> world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks)
        => Register((TWorld)world, scheduler, dependedTasks);

    public SystemHandle Register(
        TWorld world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null)
    {
        void AddDependedSystemTasks(ISystemUnion systemTypes, List<Scheduler.TaskGraphNode> result)
        {
            foreach (var systemType in systemTypes.ProxyTypes) {
                var sysData = SystemGlobalData.Get(systemType)
                    ?? throw new ArgumentException(
                        $"Failed to register system: invalid depended system type '{systemType}'");
                if (!sysData.RegisterEntries.TryGetValue((world, scheduler), out var taskNode)) {
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
                var childSysData = SystemGlobalData.Get(types[i]);
                if (childSysData == null) {
                    for (int j = 0; j != i; ++j) {
                        handles[j].Dispose();
                    }
                    throw new InvalidSystemChildException(
                        $"Failed to register system: invalid child system type '{types[i]}'");
                }
                handles[i] = childSysData.Creator!().Register(world, scheduler, childDependedTasks);
            }

            return handles;
        }

        void DoRegisterSystem(SystemGlobalData sysData, Scheduler.TaskGraphNode task)
        {
            if (!sysData.RegisterEntries.TryAdd((world, scheduler), task)) {
                throw new SystemAlreadyRegisteredException(
                    "Failed to register system: system already registered in World and Scheduler pair");
            }
            Initialize(world, scheduler);
        }

        void DoUnregisterSystem(SystemGlobalData sysData, Scheduler.TaskGraphNode task)
        {
            if (!sysData.RegisterEntries.Remove((world, scheduler), out var removedTask)) {
                throw new ObjectDisposedException("System has been disposed");
            }
            if (removedTask != task) {
                throw new ObjectDisposedException("Internal error: removed task is not the task to be disposed");
            }
            Uninitialize(world, scheduler);
        }

        var sysData = SystemGlobalData.Acquire(GetType());

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
        if (matcher == null || matcher.ProxyTypes.Length == 0) {
            task = scheduler.CreateTask(dependedTasksResult);
            task.UserData = this;

            DoRegisterSystem(sysData, task);
            childrenHandles = Children != null ? RegisterChildren(Children, task) : null;

            return new SystemHandle(
                this, task,
                handle => {
                    DoUnregisterSystem(sysData, task);

                    if (childrenHandles != null) {
                        for (int i = childrenHandles.Length - 1; i >= 0; --i) {
                            childrenHandles[i].Dispose();
                        }
                    }
                    scheduler.RemoveTask(handle.Task);
                });
        }

        var compTypes = matcher.ProxyTypes;

        Func<bool> taskFunc;
        Action disposeFunc;

        var trigger = Trigger;
        if (trigger != null) {
            var dispatcher = world.Dispatcher;
            var group = new Group<EntityRef>();
            var entityListeners = new Dictionary<EntityRef, Dispatcher.Listener>();

            var triggerTypes = new HashSet<Type>(trigger.ProxyTypes);
            bool hasAddTrigger = triggerTypes.Contains(typeof(WorldEvents.Add));
            bool hasRemoveTrigger = triggerTypes.Contains(typeof(WorldEvents.Remove));

            bool entityAddListener(in EntityRef target, IEvent e)
            {
                foreach (var compType in compTypes.AsSpan()) {
                    if (!target.Contains(compType)) {
                        return false;
                    }
                }

                bool eventListener(in EntityRef target, IEvent e)
                {
                    if (triggerTypes.Contains(e.GetType())) {
                        group.Add(target);
                    }
                    if (e is WorldEvents.Remove) {
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
                }

                dispatcher.Listen(target, eventListener);
                entityListeners.Add(target, eventListener);

                if (hasAddTrigger) {
                    group.Add(target);
                }
                return false;
            }

            dispatcher.Listen<WorldEvents.Add>(entityAddListener);

            taskFunc = () => {
                int count = group.Count;
                if (count == 0) {
                    return false;
                }
                BeforeExecute(world, scheduler);
                for (int i = 0; i < count; ++i) {
                    Execute(world, scheduler, group[i]);
                    count = group.Count;
                }
                AfterExecute(world, scheduler);
                group.Clear();
                return false;
            };

            disposeFunc = () => {
                dispatcher.Unlisten<WorldEvents.Add>(entityAddListener);
                foreach (var (entity, listener) in entityListeners) {
                    dispatcher.Unlisten(entity, listener);
                }
            };
        }
        else {
            var worldGroupCache = SystemGlobalData.WorldGroupCache;
            var cacheKey = ((World<EntityRef>)world, matcher);

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
                BeforeExecute(world, scheduler);
                foreach (var entity in span) {
                    Execute(world, scheduler, entity);
                }
                AfterExecute(world, scheduler);
                return false;
            };

            disposeFunc = () => {
                entry.RefCount--;
                if (entry.RefCount == 0) {
                    world.RemoveGroup(entry.Group);
                    if(!worldGroupCache.TryRemove(KeyValuePair.Create(cacheKey, entry))) {
                        throw new ObjectDisposedException("Failed to remove cached system group");
                    }
                }
            };
        }

        task = scheduler.CreateTask(taskFunc, dependedTasksResult);
        task.UserData = this;

        DoRegisterSystem(sysData, task);
        childrenHandles = Children != null ? RegisterChildren(Children, task) : null;

        SystemHandle? handle = null;

        void OnWorldDisposed(World<EntityRef> world) => handle!.Dispose();
        world.OnDisposed += OnWorldDisposed;

        handle = new SystemHandle(
            this, task,
            handle => {
                world.OnDisposed -= OnWorldDisposed;

                DoUnregisterSystem(sysData, task);
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