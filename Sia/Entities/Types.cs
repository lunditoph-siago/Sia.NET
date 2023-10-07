namespace Sia;

// Events

public interface IEventSender<in TEvent> : IEventSender<EntityRef, TEvent>
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

public class EventChannel<TEvent> : EventChannel<EntityRef, TEvent>, IEventSender<TEvent>
    where TEvent : IEvent
{
}

public class EventChannel : EventChannel<EntityRef, IEvent>, IEventSender
{
}

public interface IHistory<TEvent> : IHistory<EntityRef, TEvent>
    where TEvent : IEvent
{
}

public class InfiniteHistory<TEvent> : InfiniteHistory<EntityRef, TEvent>, IHistory<TEvent>
    where TEvent : IEvent
{
    public InfiniteHistory(Dispatcher<EntityRef, TEvent> dispatcher) : base(dispatcher)
    {
    }
}

public class SizedHistory<TEvent> : SizedHistory<EntityRef, TEvent>, IHistory<TEvent>
    where TEvent : IEvent
{
    public SizedHistory(Dispatcher<EntityRef, TEvent> dispatcher, int capacity) : base(dispatcher, capacity)
    {
    }
}