namespace Sia;

// Events

public interface IEventSender<in TEvent> : IEventSender<EntityRef, TEvent>
    where TEvent : IEvent;

public interface IEventSender : IEventSender<IEvent>;

public class Dispatcher<TEvent> : Dispatcher<EntityRef, TEvent>
    where TEvent : IEvent;

public class Dispatcher : Dispatcher<IEvent>;

public interface IEventListener : IEventListener<EntityRef>;

public class EventChannel<TEvent> : EventChannel<EntityRef, TEvent>, IEventSender<TEvent>
    where TEvent : IEvent;

public class EventChannel : EventChannel<EntityRef, IEvent>, IEventSender;

public class EventQueue<TEvent>(IEventSender<EntityRef, TEvent> receiver)
    : EventQueue<EntityRef, TEvent>(receiver), IEventSender<TEvent>
    where TEvent : IEvent;

public class EventQueue(IEventSender<EntityRef, IEvent> receiver)
    : EventQueue<EntityRef, IEvent>(receiver), IEventSender;

public interface IHistory<TEvent> : IHistory<EntityRef, TEvent>
    where TEvent : IEvent;

public class InfiniteHistory<TEvent>(Dispatcher<EntityRef, TEvent> dispatcher)
    : InfiniteHistory<EntityRef, TEvent>(dispatcher), IHistory<TEvent>
    where TEvent : IEvent;

public class SizedHistory<TEvent>(Dispatcher<EntityRef, TEvent> dispatcher, int capacity)
    : SizedHistory<EntityRef, TEvent>(dispatcher, capacity), IHistory<TEvent>
    where TEvent : IEvent;