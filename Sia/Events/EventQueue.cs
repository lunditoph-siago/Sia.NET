using System.Collections.Concurrent;

namespace Sia;

public class EventQueue<TTarget, TEvent>(IEventSender<TTarget, TEvent> receiver)
    : IEventSender<TTarget, TEvent>
    where TEvent : IEvent
{
    private interface ISender
    {
        void Send(IEventSender<TTarget, TEvent> receiver);
    }

    private class Sender<UEvent>(in TTarget target, in UEvent e) : ISender
        where UEvent : TEvent
    {
        public TTarget Target = target;
        public UEvent Event = e;

        public void Send(IEventSender<TTarget, TEvent> receiver)
            => receiver.Send(Target, Event);
    }

    public IEventSender<TTarget, TEvent> Receiver { get; } = receiver;

    private readonly ConcurrentQueue<ISender> _queue = new();

    public void Send<UEvent>(TTarget target, in UEvent e)
        where UEvent : TEvent
        => _queue.Enqueue(new Sender<UEvent>(target, e));

    public void Submit()
    {
        while (_queue.TryDequeue(out var sender)) {
            sender.Send(Receiver);
        }
    }
}