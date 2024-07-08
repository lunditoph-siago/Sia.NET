namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;

public abstract class EventSystemBase(SystemChain? children = null)
    : SystemBase(Matchers.Any, children: children)
{
    private interface IEventCache
    {
        public void Handle(EventSystemBase module, int index, Entity id);
        public void Clear();
    }

    private class EventCache<TEvent> : IEventCache
        where TEvent : IEvent
    {
        public readonly List<TEvent> List = [];

        public void Handle(EventSystemBase module, int index, Entity id)
        {
            ref var evt = ref List.AsSpan()[index];
            try {
                module.HandleEvent(id, evt);
            }
            catch (Exception e) {
                module.HandleException(id, evt, e);
            }
        }

        public void Clear() => List.Clear();
    }

    private readonly struct EventRecorder(EventSystemBase sys)
        : IGenericTypeHandler<IEvent>
    {
        public void Handle<T>()
            where T : IEvent
            => sys.RecordEvent<T>();
    }

    protected event Action? OnUninitialize;

    public World World { get; private set; } = null!;

    private Dictionary<Type, IEventCache> _eventCaches = [];
    private Dictionary<Type, IEventCache> _eventCachesBack = [];
    private List<(Entity, IEventCache, int)> _events = [];
    private List<(Entity, IEventCache, int)> _eventsBack = [];

    public override void Initialize(World world)
        => World = world;

    public override void Uninitialize(World world)
        => OnUninitialize?.Invoke();

    protected abstract void HandleEvent<TEvent>(Entity entity, in TEvent @event)
        where TEvent : IEvent;
    
    protected virtual void HandleException<TEvent>(Entity entity, in TEvent @event, Exception exception)
        => Console.Error.WriteLine(exception);

    protected void RecordEvent<TEvent>()
        where TEvent : IEvent
    {
        bool EventListener(Entity entity, in TEvent e)
        {
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
            _eventsBack.Add((entity, eventCache, eventIndex));
            return false;
        }

        World.Dispatcher.Listen<TEvent>(EventListener);
        OnUninitialize += () => World.Dispatcher.Unlisten<TEvent>(EventListener);
    }

    protected void RecordEvents<TEventUnion>()
        where TEventUnion : IEventUnion
        => TEventUnion.HandleEventTypes(new EventRecorder(this));

    public override void Execute(World world, IEntityQuery query)
    {
        (_eventCaches, _eventCachesBack) = (_eventCachesBack, _eventCaches);
        (_events, _eventsBack) = (_eventsBack, _events);

        try {
            foreach (var (id, cache, index) in _events) {
                cache.Handle(this, index, id);
            }
        }
        finally {
            _events.Clear();

            foreach (var cache in _eventCaches.Values) {
                cache.Clear();
            }
        }
    }
}

public abstract class SnapshotEventSystemBase<TSnapshot>(SystemChain? children = null)
    : SystemBase(Matchers.Any, children: children)
{
    private interface IEventCache
    {
        public void Handle(SnapshotEventSystemBase<TSnapshot> module, int index, Entity e);
        public void Clear();
    }

    private class EventCache<TEvent> : IEventCache
        where TEvent : IEvent
    {
        public readonly List<(TSnapshot, TEvent)> List = [];

        public void Handle(SnapshotEventSystemBase<TSnapshot> module, int index, Entity e)
        {
            ref var entry = ref List.AsSpan()[index];
            try {
                module.HandleEvent(e, entry.Item1, entry.Item2);
            }
            catch (Exception exception) {
                module.HandleException(e, entry.Item1, entry.Item2, exception);
            }
        }

        public void Clear() => List.Clear();
    }

    protected event Action? OnUninitialize;

    public World World { get; private set; } = null!;

    private Dictionary<Type, IEventCache> _eventCaches = [];
    private Dictionary<Type, IEventCache> _eventCachesBack = [];
    private List<(Entity, IEventCache, int)> _events = [];
    private List<(Entity, IEventCache, int)> _eventsBack = [];

    private readonly Dictionary<Entity, TSnapshot> _snapshots = [];

    public override void Initialize(World world)
        => World = world;

    public override void Uninitialize(World world)
        => OnUninitialize?.Invoke();
    
    protected abstract TSnapshot Snapshot<TEvent>(Entity entity, in TEvent @event)
        where TEvent : IEvent;

    protected abstract void HandleEvent<TEvent>(Entity entity, in TSnapshot snapshot, in TEvent @event)
        where TEvent : IEvent;

    protected virtual void HandleException<TEvent>(
        Entity entity, in TSnapshot snapshot, in TEvent @event, Exception exception)
        where TEvent : IEvent
        => Console.Error.WriteLine(exception);
    
    protected void RecordFor<TComponent>()
    {
        RecordOnAdded<TComponent>();
        RecordOnSet<TComponent>();
        RecordRemovalEvent<WorldEvents.Remove<TComponent>>();
    }
    
    protected void RecordOnAdded<TComponent>()
        => RecordEvent<WorldEvents.Add<TComponent>>();

    protected void RecordOnSet<TComponent>()
        => RecordEvent<WorldEvents.Set<TComponent>>();

    protected void RecordEvent<TEvent>()
        where TEvent : IEvent
    {
        bool EventListener(Entity entity, in TEvent e)
        {
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

            ref var snapshot = ref CollectionsMarshal.GetValueRefOrAddDefault(_snapshots, entity, out bool exists);
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
            _eventsBack.Add((entity, eventCache, eventIndex));
            return false;
        }

        World.Dispatcher.Listen<TEvent>(EventListener);
        OnUninitialize += () => World.Dispatcher.Unlisten<TEvent>(EventListener);
    }

    protected void RecordRemovalEvent<TEvent>()
        where TEvent : IEvent
    {
        bool EventListener(Entity entity, in TEvent e)
        {
            EventCache<TEvent> eventCache;

            var eventType = typeof(TEvent);
            if (!_eventCachesBack.TryGetValue(eventType, out var rawCache)) {
                eventCache = new();
                _eventCachesBack.Add(eventType, eventCache);
            }
            else {
                eventCache = Unsafe.As<EventCache<TEvent>>(rawCache);
            }

            if (!_snapshots.Remove(entity, out var snapshot)) {
                return false;
            }

            var eventIndex = eventCache.List.Count;
            eventCache.List.Add((snapshot, e));
            _eventsBack.Add((entity, eventCache, eventIndex));
            return false;
        }

        World.Dispatcher.Listen<TEvent>(EventListener);
        OnUninitialize += () => World.Dispatcher.Unlisten<TEvent>(EventListener);
    }

    public override void Execute(World world, IEntityQuery query)
    {
        if (_eventsBack.Count == 0) {
            return;
        }

        (_eventCaches, _eventCachesBack) = (_eventCachesBack, _eventCaches);
        (_events, _eventsBack) = (_eventsBack, _events);

        foreach (var (id, cache, index) in _events.AsSpan()) {
            cache.Handle(this, index, id);
        }

        _events.Clear();

        foreach (var cache in _eventCaches.Values) {
            cache.Clear();
        }
    }
}

public abstract class ComponentEventSystemBase<TComponent, TSnapshot>(SystemChain? children = null)
    : SnapshotEventSystemBase<TSnapshot>(children)
{
    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordFor<TComponent>();
    }
}

public abstract class ComponentEventSystemBase<TComponent>(SystemChain? children = null)
    : ComponentEventSystemBase<TComponent, TComponent>(children)
{
    protected override TComponent Snapshot<TEvent>(Entity entity, in TEvent e)
        => entity.Get<TComponent>();
}

public abstract class TemplateEventSystemBase<TComponent, TTemplate, TSnapshot>(SystemChain? children = null)
    : ComponentEventSystemBase<TSnapshot>(children)
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

    public override void Initialize(World world)
    {
        base.Initialize(world);
        TComponent.HandleCommandTypes(new CommandRecorder(this));
    }
}

public abstract class TemplateEventSystemBase<TComponent, TTemplate>(SystemChain? children = null)
    : TemplateEventSystemBase<TComponent, TTemplate, TComponent?>(children)
    where TComponent : IGeneratedByTemplate<TComponent, TTemplate>
{
    protected override TComponent? Snapshot<TEvent>(Entity entity, in TEvent e)
        => entity.Get<TComponent>();
}