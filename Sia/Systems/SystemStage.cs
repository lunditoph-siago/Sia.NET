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

    private class CollectedEntityHost : IEntityHost
    {
        public IReactiveEntityHost Host { get; }

        public event Action<IEntityHost>? OnDisposed {
            add => Host.OnDisposed += value;
            remove => Host.OnDisposed -= value;
        }

        public Type EntityType => Host.EntityType;
        public EntityDescriptor Descriptor => Host.Descriptor;

        public int Capacity => Host.Capacity;
        public int Count => _entities.Count;

        private readonly List<Entity> _entities = [];
        private readonly Dictionary<Entity, int> _entityMap = [];

        public CollectedEntityHost(IReactiveEntityHost host)
        {
            Host = host;
            Host.OnEntityReleased += (Entity e) => Remove(e.Slot);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int slot)
        {
            var index = _entities.Count;
            var entity = Host.GetEntity(slot);
            if (!_entityMap.TryAdd(entity, index)) {
                return;
            }
            _entities.Add(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(int slot)
        {
            var entity = Host.GetEntity(slot);
            if (!_entityMap.Remove(entity, out int index)) {
                return false;
            }
            int lastIndex = _entities.Count - 1;
            _entities[index] = _entities[lastIndex];
            _entities.RemoveAt(lastIndex);
            return true;
        }

        public void ClearCollected()
        {
            _entities.Clear();
            _entityMap.Clear();
        }

        public IEnumerator<Entity> GetEnumerator() => _entities.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _entities.GetEnumerator();

        public void Dispose()
        {
            _entities.Clear();
            _entityMap.Clear();
            Host.Dispose();
        }

        public Entity Create() => Host.Create();
        public void Release(int slot) => Host.Release(_entities[slot].Slot);

        public Entity GetEntity(int slot)
            => Host.GetEntity(_entities[slot].Slot);

        public void MoveOut(int slot)
            => Host.MoveOut(_entities[slot].Slot);

        public Entity Add<TComponent>(int slot, in TComponent initial)
            => Host.Add(_entities[slot].Slot, initial);

        public Entity AddMany<TList>(int slot, in TList bundle)
            where TList : IHList
            => Host.AddMany(_entities[slot].Slot, bundle);

        public Entity Set<TComponent>(int slot, in TComponent initial)
            => Host.Set(_entities[slot].Slot, initial);

        public Entity Remove<TComponent>(int slot, out bool success)
            => Host.Remove<TComponent>(_entities[slot].Slot, out success);

        public Entity RemoveMany<TList>(int slot)
            where TList : IHList
            => Host.RemoveMany<TList>(_entities[slot].Slot);

        public ref byte GetByteRef(int slot)
            => ref Host.GetByteRef(_entities[slot].Slot);

        public ref byte GetByteRef(int slot, out Entity entity)
            => ref Host.GetByteRef(_entities[slot].Slot, out entity);

        public void GetHList<THandler>(int slot, in THandler handler)
            where THandler : IRefGenericHandler<IHList>
            => Host.GetHList(_entities[slot].Slot, handler);

        public object Box(int slot)
            => Host.Box(_entities[slot].Slot);

        public IEntityHost<UEntity> GetSiblingHost<UEntity>() where UEntity : IHList
            => Host.GetSiblingHost<UEntity>();

        public void GetSiblingHostType<UEntity>(IGenericConcreteTypeHandler<IEntityHost<UEntity>> hostTypeHandler)
            where UEntity : IHList
            => Host.GetSiblingHostType(hostTypeHandler);
    }

    private record TriggerEventListener(
        ReactiveQuery Query, CollectedEntityHost Host, FrozenSet<Type> TriggerTypes) : IEventListener
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(Entity target, in TEvent e)
            where TEvent : IEvent
        {
            if (Query.Frozen) {
                return false;
            }
            var type = typeof(TEvent);
            if (TriggerTypes.Contains(type)) {
                Host.Add(target.Slot);
            }
            return false;
        }
    }

    private record FilterEventListener(
        ReactiveQuery Query, CollectedEntityHost Host, FrozenSet<Type> FilterTypes) : IEventListener
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(Entity target, in TEvent e)
            where TEvent : IEvent
        {
            if (Query.Frozen) {
                return false;
            }
            var type = typeof(TEvent);
            if (FilterTypes.Contains(type)) {
                Host.Remove(target.Slot);
            }
            return false;
        }
    }

    private record TriggerFilterEventListener(
        ReactiveQuery Query, CollectedEntityHost Host, FrozenSet<Type> TriggerTypes, FrozenSet<Type> FilterTypes) : IEventListener
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OnEvent<TEvent>(Entity target, in TEvent e)
            where TEvent : IEvent
        {
            if (Query.Frozen) {
                return false;
            }
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
            CollectedEntityHost WrappedHost, EntityHandler EntityHandler, IEventListener? Listener);
        
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

        public bool Frozen { get; set; }

        public IReadOnlyList<IEntityHost> Hosts => _hosts;
        private readonly List<CollectedEntityHost> _hosts = [];
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

            var collectedHost = new CollectedEntityHost(reactiveHost);
            _hosts.Add(collectedHost);

            var onEntityCreated = CreateEntityHandler(collectedHost, out var listener);
            reactiveHost.OnEntityCreated += onEntityCreated;
            _hostMap[reactiveHost] = new(collectedHost, onEntityCreated, listener);

            foreach (var entity in host) {
                onEntityCreated(entity);
            }
        }

        private EntityHandler CreateEntityHandler(CollectedEntityHost host, out IEventListener? resultListener)
        {
            if (_onlyHasAddEventTrigger) {
                if (_filterTypes.Count == 0) {
                    resultListener = null;
                    return target => {
                        if (Frozen) { return; }
                        host.Add(target.Slot);
                    };
                }
                else {
                    var listener = new FilterEventListener(this, host, _filterTypes);
                    resultListener = listener;
                    return target => {
                        if (Frozen) { return; }
                        host.Add(target.Slot);
                        _dispatcher.Listen(target, listener);
                    };
                }
            }
            else {
                if (_filterTypes.Count == 0) {
                    var listener = new TriggerEventListener(this, host, _triggerTypes);
                    resultListener = listener;
                    return target => _dispatcher.Listen(target, listener);
                }
                else {
                    var listener = new TriggerFilterEventListener(this, host, _triggerTypes, _filterTypes);
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
                reactiveQuery.Frozen = true;
                system.Execute(world, reactiveQuery);
                reactiveQuery.ClearCollected();
                reactiveQuery.Frozen = false;
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