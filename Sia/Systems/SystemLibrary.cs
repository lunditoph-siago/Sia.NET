namespace Sia;

using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using CommunityToolkit.HighPerformance.Buffers;
using System.Diagnostics.CodeAnalysis;

public class SystemLibrary : IAddon
{
    public class Entry
    {
        public IReadOnlyDictionary<Scheduler, Scheduler.TaskGraphNode> TaskGraphNodes => TaskGraphNodesRaw;

        internal Dictionary<Scheduler, Scheduler.TaskGraphNode> TaskGraphNodesRaw { get; } = new();
        internal HashSet<Type> RequiredPreceedingSystemTypes { get; } = new();
    }

    private class Collector : IEntityQuery
    {
        public int Count => _collectingSet.Count;
        public bool IsExecuting { get; private set; }

        private Dictionary<EntityRef, int> _collectingSet = new();
        private Dictionary<EntityRef, int> _collectedSet = new();

        private MemoryOwner<EntityRef?> _mem = MemoryOwner<EntityRef?>.Allocate(6);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginExecution()
        {
            IsExecuting = true;
            (_collectedSet, _collectingSet) = (_collectingSet, _collectedSet);

            var count = _collectedSet.Count;
            var memLenght = _mem.Length;
            if (memLenght < count) {
                do {
                    memLenght *= 2;
                } while (memLenght < count);

                _mem.Dispose();
                _mem = MemoryOwner<EntityRef?>.Allocate(memLenght);
            }

            var span = _mem.Span;
            foreach (var (entity, index) in _collectedSet) {
                span[index] = entity;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndExecution()
        {
            _collectedSet.Clear();
            IsExecuting = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in EntityRef entity)
            => _collectingSet.TryAdd(entity, _collectingSet.Count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(in EntityRef entity)
        {
            if (!IsExecuting) {
                return _collectingSet.Remove(entity);
            }
            if (_collectedSet.Remove(entity, out int index)) {
                _mem.Span[index] = null;
            }
            return true;
        }

        public void ForEach(EntityHandler handler)
        {
            foreach (ref var entity in _mem.Span[0.._collectedSet.Count]) {
                if (entity.HasValue) {
                    handler(entity.Value);
                }
            }
        }

        public void ForEach(SimpleEntityHandler handler)
        {
            foreach (ref var entity in _mem.Span[0.._collectedSet.Count]) {
                if (entity.HasValue) {
                    handler(entity.Value);
                }
            }
        }

        public void ForEach<TData>(in TData data, EntityHandler<TData> handler)
        {
            foreach (ref var entity in _mem.Span[0.._collectedSet.Count]) {
                if (entity.HasValue) {
                    handler(data, entity.Value);
                }
            }
        }

        public void ForEach<TData>(in TData data, SimpleEntityHandler<TData> handler)
        {
            foreach (ref var entity in _mem.Span[0.._collectedSet.Count]) {
                if (entity.HasValue) {
                    handler(data, entity.Value);
                }
            }
        }

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
                if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                    Collector.Add(target);
                }
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
                if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                    Collector.Add(target);
                }
            }
            else if (FilterTypes.Contains(type)) {
                if (System.OnFilterEvent(World, Scheduler, target, e)) {
                    Collector.Remove(target);
                }
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
                if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                    Collector.Add(target);
                }
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
                if (System.OnTriggerEvent(World, Scheduler, target, e)) {
                    Collector.Add(target);
                }
            }
            else if (FilterTypes.Contains(type)) {
                if (System.OnFilterEvent(World, Scheduler, target, e)) {
                    Collector.Remove(target);
                }
            }
            return false;
        }
    }

    public delegate SystemHandle SystemRegisterer(
        SystemLibrary lib, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null);

    [AllowNull] private World _world;
    private readonly Dictionary<Type, Entry> _systemEntries = new();

    public void OnInitialize(World world)
    {
        _world = world;
    }

    public Entry Get<TSystem>() where TSystem : ISystem
        => Get(typeof(TSystem));

    public Entry Get(Type systemType)
        => _systemEntries[systemType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Entry Acquire(Type systemType)
    {
        if (!_systemEntries.TryGetValue(systemType, out var instance)) {
            instance = new();
            _systemEntries.Add(systemType, instance);
        }
        return instance;
    }

    public SystemHandle Register<TSystem>(Scheduler scheduler, IEnumerable<Scheduler.TaskGraphNode>? dependedTasks = null)
        where TSystem : ISystem, new()
        => Register<TSystem>(new(), scheduler, dependedTasks);

    internal SystemHandle Register<TSystem>(TSystem system, Scheduler scheduler, IEnumerable<Scheduler.TaskGraphNode>? dependedTasks = null)
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DoRegisterSystem(Entry sysEntry, Scheduler.TaskGraphNode task)
        {
            sysEntry.TaskGraphNodesRaw.Add(scheduler, task);
            system.Initialize(_world, scheduler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DoUnregisterSystem(Entry sysEntry, Scheduler.TaskGraphNode task)
        {
            if (!sysEntry.TaskGraphNodesRaw.Remove(scheduler, out var removedTask)) {
                throw new ObjectDisposedException("System has been disposed");
            }
            if (removedTask != task) {
                throw new InvalidOperationException("Internal error: removed task is not the task to be disposed");
            }
            system.Uninitialize(_world, scheduler);
        }

        var sysEntry = Acquire(system.GetType());

        Scheduler.TaskGraphNode? task;
        IDisposable? childrenDisposable;

        var matcher = system.Matcher;
        var children = system.Children;

        if (matcher == null || matcher == Matchers.None) {
            task = dependedTasks != null
                ? scheduler.CreateTask(dependedTasks)
                : scheduler.CreateTask();
            task.UserData = system;

            DoRegisterSystem(sysEntry, task);
            childrenDisposable = children?.RegisterTo(_world, scheduler, new[] { task });

            return new SystemHandle(
                system, task,
                handle => {
                    DoUnregisterSystem(sysEntry, task);
                    childrenDisposable?.Dispose();
                    scheduler.RemoveTask(handle.TaskGraphNode);
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
                var count = collector.Count;
                if (count == 0) {
                    return false;
                }
                collector.BeginExecution();
                system.Execute(_world, scheduler, collector);
                collector.EndExecution();
                return false;
            };

            if (trigger != null && filter != null) {
                var triggerTypes = new HashSet<Type>(trigger.Types);
                var filterTypes = new HashSet<Type>(filter.Types);

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
                var triggerTypes = new HashSet<Type>(trigger.Types);

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

        DoRegisterSystem(sysEntry, task);
        childrenDisposable = children?.RegisterTo(_world, scheduler, new[] { task });

        SystemHandle? handle = null;

        void OnWorldDisposed(World world) => handle!.Dispose();
        _world.OnDisposed += OnWorldDisposed;

        handle = new SystemHandle(
            system, task,
            handle => {
                _world.OnDisposed -= OnWorldDisposed;

                DoUnregisterSystem(sysEntry, task);
                disposeFunc();
                childrenDisposable?.Dispose();
                scheduler.RemoveTask(handle.TaskGraphNode);
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
            query.OnEntityHostRemoved += host => host.OnEntityReleased -= OnEntityCreated;

            return () => {
                foreach (var host in query.Hosts) {
                    host.OnEntityReleased -= OnEntityCreated;
                }
                query.Dispose();
            };
        }
        else {
            void OnEntityCreated(in EntityRef target)
                => dispatcher.Listen(target, listener);

            query.OnEntityHostAdded += host => host.OnEntityCreated += OnEntityCreated;
            query.OnEntityHostRemoved += host => host.OnEntityReleased -= OnEntityCreated;

            return () => {
                query.ForEach((in EntityRef entity) => {
                    dispatcher.Unlisten(entity, listener);
                });
                foreach (var host in query.Hosts) {
                    host.OnEntityReleased -= OnEntityCreated;
                }
                query.Dispose();
            };
        }
    }
}