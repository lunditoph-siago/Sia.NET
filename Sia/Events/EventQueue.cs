using System.Collections.Concurrent;

namespace Sia;

public class EventQueue<TTarget, TEvent> : IEventSender<TTarget, TEvent>
    where TEvent : IEvent
{
    private interface ISender
    {
        void Send(IEventSender<TTarget, TEvent> receiver);
    }

    private class Sender<UEvent> : ISender
        where UEvent : TEvent
    {
        public TTarget Target;
        public UEvent Event;

        public Sender(in TTarget target, in UEvent e)
        {
            Target = target;
            Event = e;
        }

        public void Send(IEventSender<TTarget, TEvent> receiver)
            => receiver.Send(Target, Event);
    }

    public IEventSender<TTarget, TEvent> Receiver { get; }

    private readonly ConcurrentQueue<ISender> _queue = new();

    public EventQueue(IEventSender<TTarget, TEvent> receiver)
    {
        Receiver = receiver;
    }
    
    public void Send<UEvent>(in TTarget target, in UEvent e)
        where UEvent : TEvent
        => _queue.Enqueue(new Sender<UEvent>(target, e));

    public void Submit()
    {
        while (_queue.TryDequeue(out var sender)) {
            sender.Send(Receiver);
        }
    }
}