namespace Sia;

using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Collections;

public class SystemLibrary : IAddon
{
    public class Entry
    {
        public IReadOnlySet<Scheduler.TaskGraphNode> TaskGraphNodes => _taskGraphNodes;
        internal readonly HashSet<Scheduler.TaskGraphNode> _taskGraphNodes = [];
    }

    private class Collector : IEntityQuery
    {
        public int Count => _collectedSet.Count;
        public bool IsExecuting { get; private set; }

        internal HashSet<EntityRef> CollectingSet => _collectingSet;

        private HashSet<EntityRef> _collectingSet = [];
        private HashSet<EntityRef> _collectedSet = [];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginExecution()
        {
            IsExecuting = true;
            (_collectedSet, _collectingSet) = (_collectingSet, _collectedSet);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndExecution()
        {
            _collectedSet.Clear();
            IsExecuting = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in EntityRef entity)
            => _collectingSet.Add(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(in EntityRef entity)
            => _collectingSet.Remove(entity);

        public void ForEach(EntityHandler handler)
        {
            foreach (var entity in _collectedSet) {
                if (entity.Valid) {
                    handler(entity);
                }
            }
        }

        public void ForEach(SimpleEntityHandler handler)
        {
            foreach (var entity in _collectedSet) {
                if (entity.Valid) {
                    handler(entity);
                }
            }
        }

        public void ForEach<TData>(in TData data, EntityHandler<TData> handler)
        {
            foreach (var entity in _collectedSet) {
                if (entity.Valid) {
                    handler(data, entity);
                }
            }
        }

        public void ForEach<TData>(in TData data, SimpleEntityHandler<TData> handler)
        {
            foreach (var entity in _collectedSet) {
                if (entity.Valid) {
                    handler(data, entity);
                }
            }
        }

        public IEnumerator<EntityRef> GetEnumerator()
            => _collectedSet.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public void Dispose()
        {
        }
    }

    private record MatchAnyEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, Collector Collector,
        HashSet<Type> TriggerTypes) : IEventListener
        where TSystem : ISystem
    {
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Collector.Remove(target);
            }
            else if (TriggerTypes.Contains(e.GetType())) {
                Collector.Add(target);
            }
            return false;
        }
    }

    private record MatchAnyFilterableEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, Collector Collector,
        HashSet<Type> TriggerTypes, HashSet<Type> FilterTypes) : IEventListener
        where TSystem : ISystem
    {
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Collector.Remove(target);
            }
            else if (TriggerTypes.Contains(type)) {
                Collector.Add(target);
            }
            else if (FilterTypes.Contains(type)) {
                Collector.Remove(target);
            }
            return false;
        }
    }

    private record TargetEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, Collector Collector,
        HashSet<Type> TriggerTypes) : IEventListener
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Collector.Remove(target);
            }
            else if (TriggerTypes.Contains(e.GetType())) {
                Collector.Add(target);
            }
            return false;
        }
    }

    private record TargetFilterableEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, Collector Collector,
        HashSet<Type> TriggerTypes, HashSet<Type> FilterTypes) : IEventListener
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Collector.Remove(target);
            }
            else if (TriggerTypes.Contains(type)) {
                Collector.Add(target);
            }
            else if (FilterTypes.Contains(type)) {
                Collector.Remove(target);
            }
            return false;
        }
    }

    public delegate SystemHandle SystemRegisterer(
        SystemLibrary lib, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null);

    [AllowNull] private World _world;
    private readonly Dictionary<(Scheduler, Type), Entry> _systemEntries = [];

    public void OnInitialize(World world)
    {
        _world = world;
    }

    public Entry Get<TSystem>(Scheduler scheduler) where TSystem : ISystem
        => Get(scheduler, typeof(TSystem));

    public Entry Get(Scheduler scheduler, Type systemType)
        => _systemEntries[(scheduler, systemType)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Entry Acquire(Scheduler scheduler, Type systemType)
    {
        var key = (scheduler, systemType);
        if (!_systemEntries.TryGetValue(key, out var instance)) {
            instance = new();
            _systemEntries.Add(key, instance);
        }
        return instance;
    }

    public SystemHandle Register<TSystem>(Scheduler scheduler, IEnumerable<Scheduler.TaskGraphNode>? dependedTasks = null)
        where TSystem : ISystem, new()
        => Register<TSystem>(scheduler, () => new(), dependedTasks);

    public SystemHandle Register<TSystem>(Scheduler scheduler, Func<TSystem> creator, IEnumerable<Scheduler.TaskGraphNode>? dependedTasks = null)
        where TSystem : ISystem
    {
        var system = creator();
        var sysEntry = Acquire(scheduler, system.GetType());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InitializeSystem(Entry sysEntry, Scheduler.TaskGraphNode task)
        {
            sysEntry._taskGraphNodes.Add(task);
            system.Initialize(_world, scheduler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void UninitializeSystem(Entry sysEntry, Scheduler.TaskGraphNode task)
        {
            if (!sysEntry._taskGraphNodes.Remove(task)) {
                throw new ObjectDisposedException("Failed to unregister system: system not found");
            }
            system.Uninitialize(_world, scheduler);
        }

        Scheduler.TaskGraphNode? task;
        SystemChain.Handle? childrenDisposable;

        var matcher = system.Matcher;
        var children = system.Children;

        if (matcher == null || matcher == Matchers.None) {
            task = dependedTasks != null
                ? scheduler.CreateTask(dependedTasks)
                : scheduler.CreateTask();
            task.UserData = system;

            InitializeSystem(sysEntry, task);
            childrenDisposable = children?.RegisterTo(_world, scheduler, [task]);

            return new SystemHandle(
                system, task,
                handle => {
                    UninitializeSystem(sysEntry, task);
                    childrenDisposable?.Dispose();
                    handle.TaskGraphNode.Dispose();
                });
        }

        Func<bool> taskFunc;
        Action disposeFunc;

        var trigger = system.Trigger;
        var filter = system.Filter;
        var dispatcher = _world.Dispatcher;

        if (trigger == null && filter == null) {
            if (matcher == Matchers.Any) {
                taskFunc = () => {
                    system.Execute(_world, scheduler, _world);
                    return false;
                };
                disposeFunc = () => {};
            }
            else {
                var query = _world.Query(matcher);
                taskFunc = () => {
                    system.Execute(_world, scheduler, query);
                    return false;
                };
                disposeFunc = query.Dispose;
            }
        }
        else {
            var collector = new Collector();

            taskFunc = () => {
                var count = collector.CollectingSet.Count;
                if (count == 0) {
                    return false;
                }
                collector.BeginExecution();
                system.Execute(_world, scheduler, collector);
                collector.EndExecution();
                return false;
            };

            if (trigger != null && filter != null) {
                var triggerTypes = new HashSet<Type>(trigger.EventTypesWithPureEvents);
                var filterTypes = new HashSet<Type>(filter.EventTypesWithPureEvents);

                foreach (var filterType in filterTypes) {
                    triggerTypes.Remove(filterType);
                }

                if (matcher == Matchers.Any) {
                    var listener = new MatchAnyFilterableEventListener<TSystem>(
                        System: system,
                        World: _world,
                        Scheduler: scheduler,
                        Collector: collector,
                        TriggerTypes: triggerTypes,
                        FilterTypes: filterTypes);

                    dispatcher.Listen(listener);
                    disposeFunc = () => dispatcher.Unlisten(listener);
                }
                else {
                    disposeFunc = RegisterReactiveListener(
                        dispatcher, triggerTypes, _world.Query(matcher),
                        new TargetFilterableEventListener<TSystem>(
                            System: system,
                            World: _world,
                            Scheduler: scheduler,
                            Collector: collector,
                            TriggerTypes: triggerTypes,
                            FilterTypes: filterTypes));
                }
            }
            else if (trigger != null) {
                var triggerTypes = new HashSet<Type>(trigger.EventTypesWithPureEvents);

                if (matcher == Matchers.Any) {
                    var listener = new MatchAnyEventListener<TSystem>(
                        System: system,
                        World: _world,
                        Scheduler: scheduler,
                        Collector: collector,
                        TriggerTypes: triggerTypes);

                    dispatcher.Listen(listener);
                    disposeFunc = () => dispatcher.Unlisten(listener);
                }
                else {
                    disposeFunc = RegisterReactiveListener(
                        dispatcher, triggerTypes, _world.Query(matcher),
                        new TargetEventListener<TSystem>(
                            System: system,
                            World: _world,
                            Scheduler: scheduler,
                            Collector: collector,
                            TriggerTypes: triggerTypes));
                }
            }
            else {
                throw new InvalidSystemConfigurationException(
                    "Failed to register system: system must have non-null trigger when filter is specified");
            }
        }

        task = dependedTasks != null
            ? scheduler.CreateTask(taskFunc, dependedTasks)
            : scheduler.CreateTask(taskFunc);
        task.UserData = system;

        InitializeSystem(sysEntry, task);
        childrenDisposable = children?.RegisterTo(_world, scheduler, [task]);

        SystemHandle? handle = null;

        void OnWorldDisposed(World world) => handle!.Dispose();
        _world.OnDisposed += OnWorldDisposed;

        handle = new SystemHandle(
            system, task,
            handle => {
                _world.OnDisposed -= OnWorldDisposed;

                UninitializeSystem(sysEntry, task);
                disposeFunc();
                childrenDisposable?.Dispose();
                handle.TaskGraphNode.Terminate();
            });

        return handle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool OnlyHasAddEventTrigger(HashSet<Type> triggerTypes)
        => triggerTypes.Count == 1 && triggerTypes.Contains(typeof(WorldEvents.Add));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Action RegisterReactiveListener<TListener>(
        WorldDispatcher dispatcher, HashSet<Type> triggerTypes, World.EntityQuery query, TListener listener)
        where TListener : IEventListener
    {
        if (OnlyHasAddEventTrigger(triggerTypes)) {
            void OnEntityCreated(in EntityRef target)
                => listener.OnEvent(target, WorldEvents.Add.Instance);

            query.OnEntityHostAdded += host => host.OnEntityCreated += OnEntityCreated;
            query.OnEntityHostRemoved += host => host.OnEntityCreated -= OnEntityCreated;

            return () => {
                foreach (var host in query.Hosts) {
                    host.OnEntityCreated -= OnEntityCreated;
                }
                query.Dispose();
            };
        }
        else {
            void OnEntityCreated(in EntityRef target)
                => dispatcher.Listen(target, listener);

            query.OnEntityHostAdded += host => host.OnEntityCreated += OnEntityCreated;
            query.OnEntityHostRemoved += host => host.OnEntityCreated -= OnEntityCreated;

            return () => {
                query.ForEach((in EntityRef entity) => {
                    dispatcher.Unlisten(entity, listener);
                });
                foreach (var host in query.Hosts) {
                    host.OnEntityCreated -= OnEntityCreated;
                }
                query.Dispose();
            };
        }
    }
}