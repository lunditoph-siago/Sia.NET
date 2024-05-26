namespace Sia;

// Events

public interface IEventSender<in TEvent> : IEventSender<Entity, TEvent>
    where TEvent : IEvent
{
}

public interface IEventSender : IEventSender<IEvent> {}

public class Dispatcher<TEvent> : Dispatcher<Entity, Entity, TEvent>
    where TEvent : IEvent
{
    protected override Entity GetKey(Entity target)
        => target;
}

public class Dispatcher : Dispatcher<IEvent> {}

public interface IEventListener : IEventListener<Entity> {}

public class EventChannel<TEvent> : EventChannel<Entity, TEvent>, IEventSender<TEvent>
    where TEvent : IEvent
{
}

public class EventChannel : EventChannel<Entity, IEvent>, IEventSender
{
}

public class EventQueue<TEvent>(IEventSender<Entity, TEvent> receiver)
    : EventQueue<Entity, TEvent>(receiver), IEventSender<TEvent>
    where TEvent : IEvent
{
}

public class EventQueue(IEventSender<Entity, IEvent> receiver)
    : EventQueue<Entity, IEvent>(receiver), IEventSender
{
}