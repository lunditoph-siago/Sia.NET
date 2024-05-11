namespace Sia;

using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Collections;
using System.Collections.Frozen;
using CommunityToolkit.HighPerformance;

public class SystemLibrary : IAddon
{
    public class Entry
    {
        public IReadOnlySet<Scheduler.TaskGraphNode> TaskGraphNodes => _taskGraphNodes;
        internal readonly HashSet<Scheduler.TaskGraphNode> _taskGraphNodes = [];
    }

    private class WrappedReactiveEntityHost : IReactiveEntityHost
    {
        public IReactiveEntityHost Host { get; }

        public event EntityHandler? OnEntityCreated {
            add => Host.OnEntityCreated += value;
            remove => Host.OnEntityCreated -= value;
        }

        public event EntityHandler? OnEntityReleased {
            add => Host.OnEntityReleased += value;
            remove => Host.OnEntityReleased -= value;
        }

        public event Action<IEntityHost>? OnDisposed {
            add => Host.OnDisposed += value;
            remove => Host.OnDisposed -= value;
        }

        public Type InnerEntityType => Host.InnerEntityType;
        public EntityDescriptor Descriptor => Host.Descriptor;

        public int Capacity => Host.Capacity;
        public int Count => _entitySlots.Count;
        public ReadOnlySpan<StorageSlot> AllocatedSlots => _allocatedSlots.ValueSpan;

        private int _firstFreeSlot;

        private readonly SparseSet<StorageSlot> _allocatedSlots = [];
        private readonly Dictionary<Identity, int> _entitySlots = [];

        public WrappedReactiveEntityHost(IReactiveEntityHost host)
        {
            Host = host;
            Host.OnEntityReleased += (in EntityRef e) => Remove(e.Slot);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in StorageSlot slot)
        {
            var index = _firstFreeSlot;
            var id = Host.GetIdentity(slot);
            if (!_entitySlots.TryAdd(id, index)) {
                return;
            }
            _allocatedSlots.Add(index, slot);
            while (_allocatedSlots.ContainsKey(++_firstFreeSlot)) {}
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(in StorageSlot slot)
        {
            var id = Host.GetIdentity(slot);
            if (!_entitySlots.Remove(id, out int slotIndex)) {
                return false;
            }
            _allocatedSlots.Remove(slotIndex);
            if (_firstFreeSlot > slotIndex) {
                _firstFreeSlot = slotIndex;
            }
            return true;
        }

        public void ClearCollected()
        {
            _entitySlots.Clear();
            _allocatedSlots.Clear();
            _firstFreeSlot = 0;
        }

        public EntityRef Create() => Host.Create();
        public void Release(in StorageSlot slot) => Host.Release(slot);

        public void MoveOut(in StorageSlot slot)
            => Host.MoveOut(slot);

        public EntityRef Add<TComponent>(in StorageSlot slot, in TComponent initial)
            => Host.Add(slot, initial);

        public EntityRef AddMany<TBundle>(in StorageSlot slot, in TBundle bundle)
            where TBundle : IHList
            => Host.AddMany(slot, bundle);

        public EntityRef Remove<TComponent>(in StorageSlot slot)
            => Host.Remove<TComponent>(slot);

        public bool IsValid(in StorageSlot slot)
            => Host.IsValid(slot);

        public ref byte GetByteRef(in StorageSlot slot)
            => ref Host.GetByteRef(slot);

        public ref byte UnsafeGetByteRef(in StorageSlot slot)
            => ref Host.UnsafeGetByteRef(slot);

        public void GetHList<THandler>(in StorageSlot slot, in THandler handler)
            where THandler : IRefGenericHandler<IHList>
            => Host.GetHList(slot, handler);

        public object Box(in StorageSlot slot)
            => Host.Box(slot);

        public IEnumerator<EntityRef> GetEnumerator()
        {
            foreach (var slot in _allocatedSlots.Values) {
                yield return new(slot, Host);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            _allocatedSlots.Clear();
            _entitySlots.Clear();
            Host.Dispose();
        }
    }

    private record TriggerEventListener(
        WrappedReactiveEntityHost Host, FrozenSet<Type> TriggerTypes) : IEventListener
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (TriggerTypes.Contains(type)) {
                Host.Add(target.Slot);
            }
            return false;
        }
    }

    private record FilterEventListener(
        WrappedReactiveEntityHost Host, FrozenSet<Type> FilterTypes) : IEventListener
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (FilterTypes.Contains(type)) {
                Host.Remove(target.Slot);
            }
            return false;
        }
    }

    private record TriggerFilterEventListener(
        WrappedReactiveEntityHost Host, FrozenSet<Type> TriggerTypes, FrozenSet<Type> FilterTypes) : IEventListener
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(in EntityRef target, in TEvent e)
            where TEvent : IEvent
        {
            var type = typeof(TEvent);
            if (TriggerTypes.Contains(type)) {
                Host.Add(target.Slot);
            }
            else if (FilterTypes.Contains(type)) {
                Host.Remove(target.Slot);
            }
            return false;
        }
    }

    private class ReactiveQuery : IEntityQuery
    {
        public readonly record struct HostEntry(
            WrappedReactiveEntityHost WrappedHost, EntityHandler EntityHandler, IEventListener? Listener);
        
        public int Count {
            get {
                int count = 0;
                var span = _hosts.AsSpan();
                for (int i = 0; i != span.Length; ++i) {
                    count += span[i].Count;
                }
                return count;
            }
        }

        public IReadOnlyList<IEntityHost> Hosts => _hosts;
        private readonly List<WrappedReactiveEntityHost> _hosts = [];
        private readonly Dictionary<IEntityHost, HostEntry> _hostMap = [];

        private readonly IReactiveEntityQuery _query;
        private readonly WorldDispatcher _dispatcher;

        public readonly FrozenSet<Type> _triggerTypes;
        public readonly FrozenSet<Type> _filterTypes;

        private readonly bool _onlyHasAddEventTrigger;

        private static bool OnlyHasAddEventTrigger(FrozenSet<Type> triggerTypes)
            => triggerTypes.Count == 1 && triggerTypes.Contains(typeof(WorldEvents.Add));

        public ReactiveQuery(
            IReactiveEntityQuery query, WorldDispatcher dispatcher,
            FrozenSet<Type> triggerTypes, FrozenSet<Type> filterTypes)
        {
            _query = query;
            _dispatcher = dispatcher;

            _triggerTypes = triggerTypes;
            _filterTypes = filterTypes;

            _onlyHasAddEventTrigger = OnlyHasAddEventTrigger(triggerTypes);

            _query.OnEntityHostAdded += OnEntityHostAdded;
            _query.OnEntityHostRemoved += OnEntityHostRemoved;

            foreach (var host in _query.Hosts) {
                OnEntityHostAdded(host);
            }
        }

        private void OnEntityHostAdded(IEntityHost host)
        {
            if (host is not IReactiveEntityHost reactiveHost) {
                return;
            }

            var wrappedHost = new WrappedReactiveEntityHost(reactiveHost);
            _hosts.Add(wrappedHost);

            var onEntityCreated = CreateEntityHandler(wrappedHost, out var listener);
            reactiveHost.OnEntityCreated += onEntityCreated;
            _hostMap[reactiveHost] = new(wrappedHost, onEntityCreated, listener);

            foreach (var entity in host) {
                onEntityCreated(entity);
            }
        }

        private EntityHandler CreateEntityHandler(WrappedReactiveEntityHost host, out IEventListener? resultListener)
        {
            if (_onlyHasAddEventTrigger) {
                if (_filterTypes.Count == 0) {
                    resultListener = null;
                    return (in EntityRef target) => host.Add(target.Slot);
                }
                else {
                    var listener = new FilterEventListener(host, _filterTypes);
                    resultListener = listener;
                    return (in EntityRef target) => {
                        host.Add(target.Slot);
                        _dispatcher.Listen(target, listener);
                    };
                }
            }
            else {
                if (_filterTypes.Count == 0) {
                    var listener = new TriggerEventListener(host, _triggerTypes);
                    resultListener = listener;
                    return (in EntityRef target) => _dispatcher.Listen(target, listener);
                }
                else {
                    var listener = new TriggerFilterEventListener(host, _triggerTypes, _filterTypes);
                    resultListener = listener;
                    return (in EntityRef target) => _dispatcher.Listen(target, listener);
                }
            }
        }

        private void OnEntityHostRemoved(IEntityHost host)
        {
            if (!_hostMap.Remove(host, out var entry)) {
                return;
            }

            var wrappedHost = entry.WrappedHost;
            _hosts.Remove(wrappedHost);

            wrappedHost.ClearCollected();
            wrappedHost.Host.OnEntityCreated -= entry.EntityHandler;

            var listener = entry.Listener;
            if (listener != null) {
                foreach (var entity in wrappedHost.Host) {
                    _dispatcher.Unlisten(entity, listener);
                }
            }
        }

        public void ClearCollected()
        {
            foreach (var host in _hosts) {
                host.ClearCollected();
            }
        }

        public void Dispose()
        {
            _query.OnEntityHostAdded -= OnEntityHostAdded;
            _query.OnEntityHostRemoved -= OnEntityHostRemoved;

            foreach (var (host, (wrappedHost, handler, listener)) in _hostMap) {
                wrappedHost.ClearCollected();
                Unsafe.As<IReactiveEntityHost>(host).OnEntityCreated -= handler;

                if (listener != null) {
                    foreach (var entity in host) {
                        _dispatcher.Unlisten(entity, listener);
                    }
                }
            }

            _hostMap.Clear();
            _hosts.Clear();
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
        Action disposeFunc = () => {};

        var trigger = system.Trigger;
        var filter = system.Filter;
        var dispatcher = _world.Dispatcher;

        if (trigger == null && filter == null) {
            if (matcher == Matchers.Any) {
                taskFunc = () => {
                    system.Execute(_world, scheduler, _world);
                    return false;
                };
            }
            else {
                var query = _world.Query(matcher);
                taskFunc = () => {
                    system.Execute(_world, scheduler, query);
                    return false;
                };
            }
        }
        else {
            var filterTypes = filter?.EventTypes.TypeSet ?? FrozenSet<Type>.Empty;
            var triggerTypes = 
                (filter != null
                    ? trigger?.EventTypes.Types.Except(filter.EventTypes.TypeSet).ToFrozenSet()
                    : trigger?.EventTypes.TypeSet)
                ?? FrozenSet<Type>.Empty;

            if (triggerTypes.Count == 0) {
                throw new InvalidSystemConfigurationException(
                    "Failed to register system: reactive system must have at least one valid trigger");
            }

            var query = _world.Query(matcher);
            var reactiveQuery = new ReactiveQuery(query, dispatcher, triggerTypes, filterTypes);

            taskFunc = () => {
                if (reactiveQuery.Count == 0) {
                    return false;
                }
                system.Execute(_world, scheduler, reactiveQuery);
                reactiveQuery.ClearCollected();
                return false;
            };
            disposeFunc = reactiveQuery.Dispose;
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
}