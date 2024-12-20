namespace Sia;

using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
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
        public int Version { get; private set; }

        private readonly List<Entity> _entities = [];
        private readonly Dictionary<Entity, int> _entityMap = [];

        public CollectedEntityHost(IReactiveEntityHost host)
        {
            Host = host;
            Host.OnEntityReleased += (Entity e) => Remove(e);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(Entity entity)
        {
            var index = _entities.Count;
            if (!_entityMap.TryAdd(entity, index)) {
                return;
            }
            Version++;
            _entities.Add(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(Entity entity)
        {
            if (!_entityMap.Remove(entity, out int index)) {
                return false;
            }
            Version++;
            int lastIndex = _entities.Count - 1;
            if (index == lastIndex) {
                _entities.RemoveAt(lastIndex);
            }
            else {
                var lastEntity = _entities[lastIndex];
                _entities[index] = lastEntity;
                _entityMap[lastEntity] = index;
            }
            return true;
        }

        public void ClearCollected()
        {
            Version++;
            _entities.Clear();
            _entityMap.Clear();
        }

        public IEnumerator<Entity> GetEnumerator() => _entities.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _entities.GetEnumerator();

        public void Dispose()
        {
            Version++;
            _entities.Clear();
            _entityMap.Clear();
            Host.Dispose();
        }

        public Entity Create() => Host.Create();
        public void Release(Entity entity) => Host.Release(entity);

        public Entity GetEntity(int slot)
            => Host.GetEntity(_entities[slot].Slot);

        public void MoveOut(Entity entity)
            => Host.MoveOut(entity);

        public void Add<TComponent>(Entity entity, in TComponent initial)
            => Host.Add(entity, initial);

        public void AddMany<TList>(Entity entity, in TList bundle)
            where TList : struct, IHList
            => Host.AddMany(entity, bundle);

        public void Set<TComponent>(Entity entity, in TComponent initial)
            => Host.Set(entity, initial);

        public void Remove<TComponent>(Entity entity, out bool success)
            => Host.Remove<TComponent>(entity, out success);

        public void RemoveMany<TList>(Entity entity)
            where TList : struct, IHList
            => Host.RemoveMany<TList>(entity);

        public ref byte GetByteRef(int slot)
            => ref Host.GetByteRef(_entities[slot].Slot);

        public void GetHList<THandler>(int slot, in THandler handler)
            where THandler : IRefGenericHandler<IHList>
            => Host.GetHList(_entities[slot].Slot, handler);

        public object Box(int slot)
            => Host.Box(_entities[slot].Slot);

        public IEntityHost<UEntity> GetSiblingHost<UEntity>()
            where UEntity : struct, IHList
            => Host.GetSiblingHost<UEntity>();

        public void GetSiblingHostType<UEntity>(
            IGenericConcreteTypeHandler<IEntityHost<UEntity>> hostTypeHandler)
            where UEntity : struct, IHList
            => Host.GetSiblingHostType(hostTypeHandler);
        
        public Span<Entity> UnsafeGetEntitySpan()
            => _entities.AsSpan();
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
                Host.Add(target);
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
                Host.Remove(target);
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
                Host.Add(target);
            }
            else if (FilterTypes.Contains(type)) {
                Host.Remove(target);
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

        public int Version { get; private set; }
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
            Version++;

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
                        host.Add(target);
                    };
                }
                else {
                    var listener = new FilterEventListener(this, host, _filterTypes);
                    resultListener = listener;
                    return target => {
                        if (Frozen) { return; }
                        host.Add(target);
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
            Version++;

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

    private class CompositeDisposable(params IDisposable[] disposables) : IDisposable
    {
        private List<IDisposable>? _disposables = [..disposables];
        private readonly object _lock = new();

        public int Count {
            get {
                lock (_lock) {
                    return _disposables?.Count ?? 0;
                }
            }
        }

        public void Dispose()
        {
            List<IDisposable>? tempDisposables = null;

            lock (_lock) {
                if (_disposables is not null) {
                    tempDisposables = _disposables;
                    _disposables = null;
                }
            }

            if (tempDisposables is not null) {
                foreach (var disposable in tempDisposables) {
                    disposable.Dispose();
                }
            }
        }
    }

    public World World { get; }
    public ImmutableArray<Entry> Entries { get; }
    public bool IsDisposed { get; private set; }

    private readonly Action _combinedAction;

    internal SystemStage(World world, IEnumerable<ISystem> systems)
    {
        World = world;
        Entries = [
            ..systems.Select(system =>
                new Entry(system,
                    CreateSystemAction(world, system, out var disposable),
                    disposable))
        ];
        
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