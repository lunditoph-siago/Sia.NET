namespace Sia;

using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;

public sealed class SystemStage : IScheduleEntry, IDisposable
{
    public readonly record struct Entry(
        ISystem System, Action? Action, IDisposable? Disposable);

    private sealed class CollectedEntityHost : IEntityHost
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
        private readonly Dictionary<EntityId, int> _entityMap = [];
        private readonly EntityHandler _onEntityReleased;
        private bool _detached;

        public CollectedEntityHost(IReactiveEntityHost host)
        {
            Host = host;
            _onEntityReleased = entity => Remove(entity);
            Host.OnEntityReleased += _onEntityReleased;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(Entity entity)
        {
            var index = _entities.Count;
            if (!_entityMap.TryAdd(entity.Id, index)) {
                return;
            }
            Version++;
            _entities.Add(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(Entity entity)
        {
            if (!_entityMap.Remove(entity.Id, out var index)) {
                return false;
            }
            Version++;
            var lastIndex = _entities.Count - 1;
            if (index == lastIndex) {
                _entities.RemoveAt(lastIndex);
            }
            else {
                var lastEntity = _entities[lastIndex];
                _entities[index] = lastEntity;
                _entityMap[lastEntity.Id] = index;
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

        public void Dispose() => Detach();

        public void Detach()
        {
            if (_detached) {
                return;
            }
            _detached = true;
            Host.OnEntityReleased -= _onEntityReleased;
            ClearCollected();
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

    private sealed class ReactiveQuery : IEntityQuery
    {
        private readonly record struct HostEntry(
            CollectedEntityHost Host, EntityHandler? OnEntityCreated);

        private readonly record struct DeferredOp(
            CollectedEntityHost Host, Entity Entity, bool Collect);

        private readonly struct TriggerRegistrar(ReactiveQuery query)
            : IGenericTypeHandler<IEvent>
        {
            public void Handle<TEvent>()
                where TEvent : IEvent
                => query.ListenTrigger<TEvent>();
        }

        private readonly struct FilterRegistrar(ReactiveQuery query)
            : IGenericTypeHandler<IEvent>
        {
            public void Handle<TEvent>()
                where TEvent : IEvent
                => query.ListenFilter<TEvent>();
        }

        public int Count {
            get {
                var count = 0;
                var span = _hosts.AsSpan();
                for (var i = 0; i != span.Length; ++i) {
                    count += span[i].Count;
                }
                return count;
            }
        }

        public int Version { get; private set; }
        public bool Executing { get; private set; }

        public IReadOnlyList<IEntityHost> Hosts => _hosts;
        private readonly List<CollectedEntityHost> _hosts = [];
        private readonly Dictionary<IEntityHost, HostEntry> _hostMap = [];
        private readonly List<DeferredOp> _deferredOps = [];
        private readonly List<Action> _unlisteners = [];

        private readonly IReactiveEntityQuery _query;
        private readonly WorldDispatcher _dispatcher;

        private readonly FrozenSet<Type> _filterTypes;
        private readonly bool _collectsOnEntityCreated;

        public ReactiveQuery(
            IReactiveEntityQuery query, WorldDispatcher dispatcher,
            IEventUnion? trigger, IEventUnion? filter)
        {
            _query = query;
            _dispatcher = dispatcher;

            var triggerTypes = trigger?.EventTypes.TypeSet ?? FrozenSet<Type>.Empty;
            _filterTypes = filter?.EventTypes.TypeSet ?? FrozenSet<Type>.Empty;
            var validTriggerCount = triggerTypes.Count(type => !_filterTypes.Contains(type));
            if (validTriggerCount == 0) {
                throw new InvalidSystemConfigurationException(
                    "Invalid system configuration: reactive system must have at least one valid trigger");
            }

            _collectsOnEntityCreated = validTriggerCount == 1
                && triggerTypes.Contains(typeof(WorldEvents.Add))
                && !_filterTypes.Contains(typeof(WorldEvents.Add));

            try {
                _query.OnEntityHostAdded += OnEntityHostAdded;
                _query.OnEntityHostRemoved += OnEntityHostRemoved;

                foreach (var host in _query.Hosts) {
                    OnEntityHostAdded(host);
                }
                if (!_collectsOnEntityCreated) {
                    trigger!.Handle(new TriggerRegistrar(this));
                }
                if (_filterTypes.Count != 0) {
                    filter!.Handle(new FilterRegistrar(this));
                }
            }
            catch (Exception attachmentError) {
                Outcome<Exception>.Failure(attachmentError)
                    .Attempt(Dispose)
                    .ThrowFailure();
            }
        }

        private void ListenTrigger<TEvent>()
            where TEvent : IEvent
        {
            if (_filterTypes.Contains(typeof(TEvent))) {
                return;
            }
            WorldDispatcher.Listener<TEvent> listener = OnTrigger;
            _dispatcher.Listen(listener);
            _unlisteners.Add(() => _dispatcher.Unlisten(listener));
        }

        private void ListenFilter<TEvent>()
            where TEvent : IEvent
        {
            WorldDispatcher.Listener<TEvent> listener = OnFilter;
            _dispatcher.Listen(listener);
            _unlisteners.Add(() => _dispatcher.Unlisten(listener));
        }

        private bool OnTrigger<TEvent>(Entity target, in TEvent e)
            where TEvent : IEvent
        {
            if (!Executing && target.Host is { } host
                && _hostMap.TryGetValue(host, out var entry)) {
                entry.Host.Add(target);
            }
            return false;
        }

        private bool OnFilter<TEvent>(Entity target, in TEvent e)
            where TEvent : IEvent
        {
            if (target.Host is { } host && _hostMap.TryGetValue(host, out var entry)) {
                Uncollect(entry.Host, target);
            }
            return false;
        }

        public void BeginExecution() => Executing = true;

        public void EndExecution()
        {
            var result = Outcome<Exception>.Success;
            foreach (var host in _hosts) {
                result = result.Attempt(host.ClearCollected);
            }
            Executing = false;
            foreach (var op in _deferredOps) {
                if (!op.Collect) {
                    result = result.Attempt(() => op.Host.Remove(op.Entity));
                }
                else if (op.Entity.IsValid) {
                    result = result.Attempt(() => op.Host.Add(op.Entity));
                }
            }
            _deferredOps.Clear();
            result.ThrowIfFailed();
        }

        private void Collect(CollectedEntityHost host, Entity entity)
        {
            if (Executing) {
                _deferredOps.Add(new(host, entity, Collect: true));
            }
            else {
                host.Add(entity);
            }
        }

        private void Uncollect(CollectedEntityHost host, Entity entity)
        {
            if (Executing) {
                _deferredOps.Add(new(host, entity, Collect: false));
            }
            else {
                host.Remove(entity);
            }
        }

        private void OnEntityHostAdded(IReactiveEntityHost host)
        {
            Version++;
            var collectedHost = new CollectedEntityHost(host);
            _hosts.Add(collectedHost);

            EntityHandler? onEntityCreated = null;
            try {
                if (_collectsOnEntityCreated) {
                    onEntityCreated = entity => Collect(collectedHost, entity);
                    host.OnEntityCreated += onEntityCreated;
                }
                _hostMap.Add(host, new(collectedHost, onEntityCreated));

                if (onEntityCreated != null) {
                    foreach (var entity in host) {
                        onEntityCreated(entity);
                    }
                }
            }
            catch (Exception error) {
                Outcome<Exception>.Failure(error)
                    .Attempt(() => _hostMap.Remove(host))
                    .Attempt(() => _hosts.Remove(collectedHost))
                    .Attempt(() => {
                        if (onEntityCreated != null) {
                            host.OnEntityCreated -= onEntityCreated;
                        }
                    })
                    .Attempt(collectedHost.Detach)
                    .ThrowFailure();
            }
        }

        private void OnEntityHostRemoved(IReactiveEntityHost host)
        {
            if (!_hostMap.Remove(host, out var entry)) {
                return;
            }
            Version++;
            _hosts.Remove(entry.Host);
            DetachHost(entry);
        }

        public void Dispose()
        {
            _query.OnEntityHostAdded -= OnEntityHostAdded;
            _query.OnEntityHostRemoved -= OnEntityHostRemoved;

            var result = Outcome<Exception>.Success;
            foreach (var unlisten in _unlisteners) {
                result = result.Attempt(unlisten);
            }
            _unlisteners.Clear();
            foreach (var entry in _hostMap.Values) {
                result = result.Attempt(() => DetachHost(entry));
            }
            _hostMap.Clear();
            _hosts.Clear();
            _deferredOps.Clear();
            result.ThrowIfFailed();
        }

        private static void DetachHost(in HostEntry entry)
        {
            entry.Host.Detach();
            if (entry.OnEntityCreated is { } onEntityCreated) {
                entry.Host.Host.OnEntityCreated -= onEntityCreated;
            }
        }
    }

    private sealed class CompositeDisposable(params IDisposable[] disposables)
        : IDisposable
    {
        private IDisposable[]? _disposables = disposables;

        public int Count => Volatile.Read(ref _disposables)?.Length ?? 0;

        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _disposables, null);
            var result = Outcome<Exception>.Success;
            for (var i = (owned?.Length ?? 0) - 1; i >= 0; i--) {
                var disposable = owned![i];
                result = result.Attempt(disposable.Dispose);
            }
            result.ThrowIfFailed();
        }
    }

    public World World { get; }
    public ImmutableArray<Entry> Entries { get; }
    public bool IsDisposed { get; private set; }

    private readonly Action _combinedAction;

    internal SystemStage(World world, IEnumerable<ISystem> systems)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(systems);
        World = world;
        var entries = ImmutableArray.CreateBuilder<Entry>();
        try {
            foreach (var system in systems) {
                var action = CreateSystemAction(
                    world, system, out var disposable);
                entries.Add(new(system, action, disposable));
            }
        }
        catch (Exception compilationError) {
            DisposeResources(entries, Outcome<Exception>.Failure(compilationError))
                .ThrowFailure();
        }

        Entries = entries.ToImmutable();
        _combinedAction = ComposeActions(Entries);

        for (var i = 0; i < Entries.Length; i++) {
            try {
                Entries[i].System.Initialize(world);
            }
            catch (Exception initializationError) {
                IsDisposed = true;
                CleanupInitialized(
                    world, Entries, i, Outcome<Exception>.Failure(initializationError))
                    .ThrowFailure();
            }
        }
    }

    public SystemStage(World world, ExecutionPlan plan)
        : this(world, (plan ?? throw new ArgumentNullException(nameof(plan)))
            .Entries.Select(static entry => entry.Creator()))
    { }

    public void Dispose()
    {
        if (IsDisposed) {
            return;
        }
        IsDisposed = true;
        CleanupInitialized(World, Entries, Entries.Length, Outcome<Exception>.Success)
            .ThrowIfFailed();
    }

    public void Tick()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _combinedAction();
    }

    private static Outcome<Exception> CleanupInitialized(
        World world,
        IReadOnlyList<Entry> entries,
        int initializedCount,
        Outcome<Exception> result)
    {
        for (var i = initializedCount - 1; i >= 0; i--) {
            var system = entries[i].System;
            result = result.Attempt(() => system.Uninitialize(world));
        }
        return DisposeResources(entries, result);
    }

    private static Outcome<Exception> DisposeResources(
        IReadOnlyList<Entry> entries,
        Outcome<Exception> result)
    {
        for (var i = entries.Count - 1; i >= 0; i--) {
            if (entries[i].Disposable is { } disposable) {
                result = result.Attempt(disposable.Dispose);
            }
        }
        return result;
    }

    private static Action ComposeActions(ImmutableArray<Entry> entries)
    {
        var actions = entries
            .Where(static entry => entry.Action != null)
            .Select(static entry => entry.Action!)
            .ToArray();

        return actions.Length switch {
            0 => static () => { },
            1 => actions[0],
            _ => () => {
                foreach (var action in actions) {
                    action();
                }
            }
        };
    }

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
            var query = world.Query(matcher);
            var reactiveQuery = new ReactiveQuery(query, dispatcher, trigger, filter);

            selfDisposable = reactiveQuery;
            selfAction = () => {
                if (reactiveQuery.Count == 0) {
                    return;
                }
                reactiveQuery.BeginExecution();
                try {
                    system.Execute(world, reactiveQuery);
                }
                finally {
                    reactiveQuery.EndExecution();
                }
            };
        }

        if (children != null) {
            SystemStage childrenStage;
            try {
                childrenStage = children.CreateStage(world);
            }
            catch (Exception creationError) {
                var result = Outcome<Exception>.Failure(creationError);
                if (selfDisposable != null) {
                    result = result.Attempt(selfDisposable.Dispose);
                }
                childrenStage = result.ThrowFailure<SystemStage>();
            }
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
