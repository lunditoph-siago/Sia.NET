namespace Sia;

using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public sealed class SystemStage : IDisposable
{
    public record struct Entry(ISystem System, Action? Action, IDisposable? Disposable);

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

        public Type EntityType => Host.EntityType;
        public EntityDescriptor Descriptor => Host.Descriptor;

        public int Capacity => Host.Capacity;
        public int Count => _entitySlots.Count;
        public ReadOnlySpan<StorageSlot> AllocatedSlots => _allocatedSlots.ValueSpan;

        private int _firstFreeSlot;

        private readonly SparseSet<StorageSlot> _allocatedSlots = [];
        private readonly Dictionary<Entity, int> _entitySlots = [];

        public WrappedReactiveEntityHost(IReactiveEntityHost host)
        {
            Host = host;
            Host.OnEntityReleased += (Entity e) => Remove(e.Slot);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in StorageSlot slot)
        {
            var index = _firstFreeSlot;
            var entity = Host.GetEntity(slot);
            if (!_entitySlots.TryAdd(entity, index)) {
                return;
            }
            _allocatedSlots.Add(index, slot);
            while (_allocatedSlots.ContainsKey(++_firstFreeSlot)) {}
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(in StorageSlot slot)
        {
            var entity = Host.GetEntity(slot);
            if (!_entitySlots.Remove(entity, out int slotIndex)) {
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

        public Entity Create() => Host.Create();
        public void Release(in StorageSlot slot) => Host.Release(slot);

        public Entity GetEntity(in StorageSlot slot)
            => Host.GetEntity(slot);

        public void MoveOut(in StorageSlot slot)
            => Host.MoveOut(slot);

        public Entity Add<TComponent>(in StorageSlot slot, in TComponent initial)
            => Host.Add(slot, initial);

        public Entity AddMany<TList>(in StorageSlot slot, in TList bundle)
            where TList : IHList
            => Host.AddMany(slot, bundle);

        public Entity Set<TComponent>(in StorageSlot slot, in TComponent initial)
            => Host.Set(slot, initial);

        public Entity Remove<TComponent>(in StorageSlot slot, out bool success)
            => Host.Remove<TComponent>(slot, out success);

        public Entity RemoveMany<TList>(in StorageSlot slot)
            where TList : IHList
            => Host.RemoveMany<TList>(slot);

        public bool IsValid(in StorageSlot slot)
            => Host.IsValid(slot);

        public ref byte GetByteRef(in StorageSlot slot)
            => ref Host.GetByteRef(slot);

        public ref byte GetByteRef(in StorageSlot slot, out Entity entity)
            => ref Host.GetByteRef(slot, out entity);

        public ref byte UnsafeGetByteRef(in StorageSlot slot)
            => ref Host.UnsafeGetByteRef(slot);

        public ref byte UnsafeGetByteRef(in StorageSlot slot, out Entity entity)
            => ref Host.UnsafeGetByteRef(slot, out entity);

        public void GetHList<THandler>(in StorageSlot slot, in THandler handler)
            where THandler : IRefGenericHandler<IHList>
            => Host.GetHList(slot, handler);

        public object Box(in StorageSlot slot)
            => Host.Box(slot);

        public IEnumerator<Entity> GetEnumerator()
        {
            foreach (var slot in _allocatedSlots.Values) {
                yield return Host.GetEntity(slot);
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
        public bool OnEvent<TEvent>(Entity target, in TEvent e)
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
        public bool OnEvent<TEvent>(Entity target, in TEvent e)
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
        public bool OnEvent<TEvent>(Entity target, in TEvent e)
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
                    return target => host.Add(target.Slot);
                }
                else {
                    var listener = new FilterEventListener(host, _filterTypes);
                    resultListener = listener;
                    return target => {
                        host.Add(target.Slot);
                        _dispatcher.Listen(target, listener);
                    };
                }
            }
            else {
                if (_filterTypes.Count == 0) {
                    var listener = new TriggerEventListener(host, _triggerTypes);
                    resultListener = listener;
                    return target => _dispatcher.Listen(target, listener);
                }
                else {
                    var listener = new TriggerFilterEventListener(host, _triggerTypes, _filterTypes);
                    resultListener = listener;
                    return target => _dispatcher.Listen(target, listener);
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

    public World World { get; }
    public ImmutableArray<Entry> Entries { get; }
    public bool IsDisposed { get; private set; }

    private readonly Action _combinedAction;

    internal SystemStage(World world, IEnumerable<ISystem> systems)
    {
        World = world;
        Entries = systems.Select(system =>
            new Entry(system,
                CreateSystemAction(world, system, out var disposable),
                disposable))
            .ToImmutableArray();
        
        var actions = Entries
            .Where(entry => entry.Action != null)
            .Select(entry => entry.Action!)
            .ToArray();

        _combinedAction = () => {
            foreach (var action in actions) {
                action();
            }
        };

        foreach (var entry in Entries) {
            entry.System.Initialize(world);
        }
    }

    ~SystemStage()
    {
        DoDispose();
    }

    public void Dispose()
    {
        DoDispose();
        GC.SuppressFinalize(this);
    }

    private void DoDispose()
    {
        if (IsDisposed) {
            return;
        }
        foreach (var entry in Entries) {
            entry.System.Uninitialize(World);
            entry.Disposable?.Dispose();
        }
        IsDisposed = true;
    }

    public void Tick() => _combinedAction();

    private static Action? CreateSystemAction(World world, ISystem system, out IDisposable? disposable)
    {
        var matcher = system.Matcher;
        var children = system.Children;

        if (matcher == null || matcher == Matchers.None) {
            if (children == null) {
                disposable = null;
                return null;
            }
            else {
                var childrenStage = children.CreateStage(world);
                disposable = childrenStage;
                return childrenStage.Tick;
            }
        }

        var trigger = system.Trigger;
        var filter = system.Filter;
        var dispatcher = world.Dispatcher;

        Action? selfAction;
        IDisposable? selfDisposable;

        if (trigger == null && filter == null) {
            if (matcher == Matchers.Any) {
                selfDisposable = null;
                selfAction = () => system.Execute(world, world);
            }
            else {
                var query = world.Query(matcher);
                selfDisposable = null;
                selfAction = () => system.Execute(world, query);
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
                    "Invalid system configuration: reactive system must have at least one valid trigger");
            }

            var query = world.Query(matcher);
            var reactiveQuery = new ReactiveQuery(query, dispatcher, triggerTypes, filterTypes);

            selfDisposable = reactiveQuery;
            selfAction = () => {
                if (reactiveQuery.Count == 0) {
                    return;
                }
                system.Execute(world, reactiveQuery);
                reactiveQuery.ClearCollected();
            };
        }

        if (children != null) {
            var childrenStage = children.CreateStage(world);
            var childrenStageAction = childrenStage._combinedAction;

            disposable = selfDisposable != null
                ? new CompositeDisposable(selfDisposable, childrenStage)
                : childrenStage;

            return () => {
                selfAction();
                childrenStageAction();
            };
        }
        else {
            disposable = selfDisposable;
            return selfAction;
        }
    }
}