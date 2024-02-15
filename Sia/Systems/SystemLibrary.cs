namespace Sia;

using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.HighPerformance.Buffers;
using System.Collections;

public class SystemLibrary : IAddon
{
    public class Entry
    {
        public IReadOnlySet<Scheduler.TaskGraphNode> TaskGraphNodes => _taskGraphNodes;
        internal readonly HashSet<Scheduler.TaskGraphNode> _taskGraphNodes = [];
    }

    private class ReactiveEntityHost : IEntityHost
    {
        public int Capacity => int.MaxValue;
        public int Count { get; private set; }
        public ReadOnlySpan<StorageSlot> AllocatedSlots => _allocatedSlots.ValueSpan;

        internal bool IsExecuting { get; private set; }

        private readonly BucketBuffer<(IEntityHost Host, int Slot)> _buffer = new();
        private readonly SparseSet<StorageSlot> _allocatedSlots = [];
        private readonly Dictionary<EntityRef, int> _entitySlots = [];

        private int _firstFreeSlot;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginExecution()
        {
            IsExecuting = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndExecution()
        {
            _buffer.Clear();
            _allocatedSlots.Clear();
            IsExecuting = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in EntityRef entity)
        {
            var slot = _firstFreeSlot;
            _allocatedSlots.Add(slot, new(slot, entity.Version));
            while (_allocatedSlots.ContainsKey(++_firstFreeSlot)) {}

            _buffer.CreateRef(slot) = (entity.Host, entity.Slot);
            _entitySlots[entity] = slot;

            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(in EntityRef entity)
        {
            if (!_entitySlots.Remove(entity, out int slot)) {
                return false;
            }
            _buffer.Release(slot);
            _allocatedSlots.Remove(slot);

            Count--;
            if (_firstFreeSlot > slot) {
                _firstFreeSlot = slot;
            }
            return true;
        }

        public bool ContainsCommon<TComponent>() => false;
        public bool ContainsCommon(Type componentType) => false;

        public EntityRef Create() => throw new NotSupportedException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Release(int slot, int version)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            host.Release(innerSlot, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(int slot, int version)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            return host.IsValid(innerSlot, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains<TComponent>(int slot, int version)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            return host.Contains<TComponent>(innerSlot, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int slot, int version, Type componentType)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            return host.Contains(innerSlot, version, componentType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref TComponent Get<TComponent>(int slot, int version)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            return ref host.Get<TComponent>(innerSlot, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref TComponent GetOrNullRef<TComponent>(int slot, int version)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            return ref host.GetOrNullRef<TComponent>(innerSlot, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityDescriptor GetDescriptor(int slot, int version)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            return host.GetDescriptor(innerSlot, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object Box(int slot, int version)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            return host.Box(innerSlot, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetSpan(int slot, int version)
        {
            var (host, innerSlot) = _buffer.GetRef(slot);
            return host.GetSpan(innerSlot, version);
        }

        public IEnumerator<EntityRef> GetEnumerator() => _entitySlots.Keys.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            _buffer.Dispose();
            _allocatedSlots.Clear();
            _entitySlots.Clear();
        }
    }

    private record MatchAnyEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, ReactiveEntityHost Host,
        HashSet<Type> TriggerTypes) : IEventListener
        where TSystem : ISystem
    {
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Host.Remove(target);
            }
            else if (TriggerTypes.Contains(e.GetType())) {
                Host.Add(target);
            }
            return false;
        }
    }

    private record MatchAnyFilterableEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, ReactiveEntityHost Host,
        HashSet<Type> TriggerTypes, HashSet<Type> FilterTypes) : IEventListener
        where TSystem : ISystem
    {
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Host.Remove(target);
            }
            else if (TriggerTypes.Contains(type)) {
                Host.Add(target);
            }
            else if (FilterTypes.Contains(type)) {
                Host.Remove(target);
            }
            return false;
        }
    }

    private record TargetEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, ReactiveEntityHost Host,
        HashSet<Type> TriggerTypes) : IEventListener
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Host.Remove(target);
            }
            else if (TriggerTypes.Contains(e.GetType())) {
                Host.Add(target);
            }
            return false;
        }
    }

    private record TargetFilterableEventListener<TSystem>(
        TSystem System, World World, Scheduler Scheduler, ReactiveEntityHost Host,
        HashSet<Type> TriggerTypes, HashSet<Type> FilterTypes) : IEventListener
        where TSystem : ISystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (type == typeof(WorldEvents.Remove)) {
                Host.Remove(target);
            }
            else if (TriggerTypes.Contains(type)) {
                Host.Add(target);
            }
            else if (FilterTypes.Contains(type)) {
                Host.Remove(target);
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
            var reactiveHost = new ReactiveEntityHost();
            var query = new EntityQuery([reactiveHost]);

            taskFunc = () => {
                var count = reactiveHost.Count;
                if (count == 0) {
                    return false;
                }
                reactiveHost.BeginExecution();
                system.Execute(_world, scheduler, query);
                reactiveHost.EndExecution();
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
                        Host: reactiveHost,
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
                            Host: reactiveHost,
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
                        Host: reactiveHost,
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
                            Host: reactiveHost,
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
                foreach (var entity in query) {
                    dispatcher.Unlisten(entity, listener);
                }
                foreach (var host in query.Hosts) {
                    host.OnEntityCreated -= OnEntityCreated;
                }
                query.Dispose();
            };
        }
    }
}