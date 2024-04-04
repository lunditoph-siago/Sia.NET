namespace Sia;

// Events

public interface IEventSender<in TEvent> : IEventSender<EntityRef, TEvent>
    where TEvent : IEvent
{
}

public interface IEventSender : IEventSender<IEvent> {}

public class Dispatcher<TEvent> : Dispatcher<EntityRef, Identity, TEvent>
    where TEvent : IEvent
{
    protected override Identity GetKey(in EntityRef target)
        => target.Id;
}

public class Dispatcher : Dispatcher<IEvent> {}

public interface IEventListener : IEventListener<EntityRef> {}

public class EventChannel<TEvent> : EventChannel<EntityRef, TEvent>, IEventSender<TEvent>
    where TEvent : IEvent
{
}

public class EventChannel : EventChannel<EntityRef, IEvent>, IEventSender
{
}

public class EventQueue<TEvent>(IEventSender<EntityRef, TEvent> receiver)
    : EventQueue<EntityRef, TEvent>(receiver), IEventSender<TEvent>
    where TEvent : IEvent
{
}

public class EventQueue(IEventSender<EntityRef, IEvent> receiver)
    : EventQueue<EntityRef, IEvent>(receiver), IEventSender
{
}