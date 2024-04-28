namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;

public abstract class EventSystemBase(
    SystemChain? children = null)
    : SystemBase(Matchers.Any, children: children)
{
    private interface IEventCache
    {
        public void Handle(EventSystemBase module, int index, in Identity id);
        public void Clear();
    }

    private class EventCache<TEvent> : IEventCache
        where TEvent : IEvent
    {
        public readonly List<TEvent> List = [];

        public void Handle(EventSystemBase module, int index, in Identity id)
        {
            ref var entity = ref List.AsSpan()[index];
            module.HandleEvent(id, entity);
        }

        public void Clear() => List.Clear();
    }

    protected event Action? OnUninitialize;

    public World World { get; private set; } = null!;
    public Scheduler Scheduler { get; private set; } = null!;

    private Dictionary<Type, IEventCache> _eventCaches = [];
    private Dictionary<Type, IEventCache> _eventCachesBack = [];
    private List<(Identity, IEventCache, int)> _events = [];
    private List<(Identity, IEventCache, int)> _eventsBack = [];

    public override void Initialize(World world, Scheduler scheduler)
    {
        World = world;
        Scheduler = scheduler;
    }

    public override void Uninitialize(World world, Scheduler scheduler)
        => OnUninitialize?.Invoke();

    protected abstract void HandleEvent<TEvent>(
        in Identity id, in TEvent e)
        where TEvent : IEvent;

    protected void RecordEvent<TEvent>()
        where TEvent : IEvent
    {
        bool EventListener(in EntityRef entity, in TEvent e)
        {
            var id = entity.Id;
            EventCache<TEvent> eventCache;

            var eventType = typeof(TEvent);
            if (!_eventCachesBack.TryGetValue(eventType, out var rawCache)) {
                eventCache = new();
                _eventCachesBack.Add(eventType, eventCache);
            }
            else {
                eventCache = Unsafe.As<EventCache<TEvent>>(rawCache);
            }

            var eventIndex = eventCache.List.Count;
            eventCache.List.Add(e);
            _eventsBack.Add((id, eventCache, eventIndex));
            return false;
        }

        World.Dispatcher.Listen<TEvent>(EventListener);
        OnUninitialize += () => World.Dispatcher.Unlisten<TEvent>(EventListener);
    }

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
    {
        (_eventCaches, _eventCachesBack) = (_eventCachesBack, _eventCaches);
        (_events, _eventsBack) = (_eventsBack, _events);

        foreach (var (id, cache, index) in _events) {
            cache.Handle(this, index, id);
        }

        _events.Clear();

        foreach (var cache in _eventCaches.Values) {
            cache.Clear();
        }
    }
}

public abstract class SnapshotEventSystemBase<TSnapshot>(
    SystemChain? children = null)
    : SystemBase(Matchers.Any, children: children)
{
    private interface IEventCache
    {
        public void Handle(SnapshotEventSystemBase<TSnapshot> module, int index, in Identity id);
        public void Clear();
    }

    private class EventCache<TEvent> : IEventCache
        where TEvent : IEvent
    {
        public readonly List<(TSnapshot, TEvent)> List = [];

        public void Handle(SnapshotEventSystemBase<TSnapshot> module, int index, in Identity id)
        {
            ref var entry = ref List.AsSpan()[index];
            module.HandleEvent(id, entry.Item1, entry.Item2);
        }

        public void Clear() => List.Clear();
    }

    protected event Action? OnUninitialize;

    public World World { get; private set; } = null!;
    public Scheduler Scheduler { get; private set; } = null!;

    private Dictionary<Type, IEventCache> _eventCaches = [];
    private Dictionary<Type, IEventCache> _eventCachesBack = [];
    private List<(Identity, IEventCache, int)> _events = [];
    private List<(Identity, IEventCache, int)> _eventsBack = [];

    private readonly Dictionary<Identity, TSnapshot> _snapshots = [];

    public override void Initialize(World world, Scheduler scheduler)
    {
        World = world;
        Scheduler = scheduler;
    }

    public override void Uninitialize(World world, Scheduler scheduler)
        => OnUninitialize?.Invoke();
    
    protected abstract TSnapshot Snapshot<TEvent>(
        in EntityRef entity, in TEvent e)
        where TEvent : IEvent;

    protected abstract void HandleEvent<TEvent>(
        in Identity id, in TSnapshot snapshot, in TEvent e)
        where TEvent : IEvent;

    protected void RecordEvent<TEvent>()
        where TEvent : IEvent
    {
        bool EventListener(in EntityRef entity, in TEvent e)
        {
            var id = entity.Id;
            EventCache<TEvent> eventCache;

            var eventType = typeof(TEvent);
            if (!_eventCachesBack.TryGetValue(eventType, out var rawCache)) {
                eventCache = new();
                _eventCachesBack.Add(eventType, eventCache);
            }
            else {
                eventCache = Unsafe.As<EventCache<TEvent>>(rawCache);
            }

            TSnapshot lastSnapshot;

            ref var snapshot = ref CollectionsMarshal.GetValueRefOrAddDefault(_snapshots, id, out bool exists);
            if (!exists) {
                snapshot = Snapshot(entity, e);
                lastSnapshot = snapshot;
            }
            else {
                lastSnapshot = snapshot!;
                snapshot = Snapshot(entity, e);
            }

            var eventIndex = eventCache.List.Count;
            eventCache.List.Add((lastSnapshot, e));
            _eventsBack.Add((id, eventCache, eventIndex));
            return false;
        }

        World.Dispatcher.Listen<TEvent>(EventListener);
        OnUninitialize += () => World.Dispatcher.Unlisten<TEvent>(EventListener);
    }

    protected void RecordRemovalEvent<TEvent>()
        where TEvent : IEvent
    {
        bool EventListener(in EntityRef entity, in TEvent e)
        {
            var id = entity.Id;
            EventCache<TEvent> eventCache;

            var eventType = typeof(TEvent);
            if (!_eventCachesBack.TryGetValue(eventType, out var rawCache)) {
                eventCache = new();
                _eventCachesBack.Add(eventType, eventCache);
            }
            else {
                eventCache = Unsafe.As<EventCache<TEvent>>(rawCache);
            }

            if (!_snapshots.Remove(id, out var snapshot)) {
                return false;
            }

            var eventIndex = eventCache.List.Count;
            eventCache.List.Add((snapshot, e));
            _eventsBack.Add((id, eventCache, eventIndex));
            return false;
        }

        World.Dispatcher.Listen<TEvent>(EventListener);
        OnUninitialize += () => World.Dispatcher.Unlisten<TEvent>(EventListener);
    }

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
    {
        (_eventCaches, _eventCachesBack) = (_eventCachesBack, _eventCaches);
        (_events, _eventsBack) = (_eventsBack, _events);

        foreach (var (id, cache, index) in _events) {
            cache.Handle(this, index, id);
        }

        _events.Clear();

        foreach (var cache in _eventCaches.Values) {
            cache.Clear();
        }
    }
}

public abstract class TemplateEventSystemBase<TComponent, TTemplate, TSnapshot>(
    SystemChain? children = null)
    : SnapshotEventSystemBase<TSnapshot>(children)
    where TComponent : IGeneratedByTemplate<TComponent, TTemplate>
{
    private readonly struct CommandRecorder(
        TemplateEventSystemBase<TComponent, TTemplate, TSnapshot> sys)
        : IGenericTypeHandler<ICommand<TComponent>>
    {
        public void Handle<T>()
            where T : ICommand<TComponent>
            => sys.RecordEvent<T>();
    }

    public override void Initialize(World world, Scheduler scheduler)
    {
        base.Initialize(world, scheduler);
        World.IndexHosts(Matchers.Of<TComponent>());

        RecordEvent<WorldEvents.Add<TComponent>>();
        RecordRemovalEvent<WorldEvents.Remove<TComponent>>();
        TComponent.HandleCommandTypes(new CommandRecorder(this));
    }
}

public abstract class TemplateEventSystemBase<TComponent, TTemplate>(
    SystemChain? children = null)
    : TemplateEventSystemBase<TComponent, TTemplate, TComponent?>(children)
    where TComponent : IGeneratedByTemplate<TComponent, TTemplate>
{
    protected override TComponent? Snapshot<TEvent>(in EntityRef entity, in TEvent e)
        => entity.Get<TComponent>();
}