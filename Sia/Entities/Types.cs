namespace Sia;

public class Group : Group<EntityRef> {}

// Events

public interface IEventSender<TEvent> : IEventSender<EntityRef, TEvent>
    where TEvent : IEvent
{
}

public interface IEventSender : IEventSender<IEvent> {}

public class Dispatcher<TEvent> : Dispatcher<EntityRef, TEvent>
    where TEvent : IEvent
{
}

public class Dispatcher : Dispatcher<IEvent> {}

public interface IEventListener : IEventListener<EntityRef> {}

public class EventMailbox<TEvent> : EventMailbox<EntityRef, TEvent>, IEventSender<TEvent>
    where TEvent : IEvent
{
}

public class EventMailbox : EventMailbox<EntityRef, IEvent>, IEventSender
{
}