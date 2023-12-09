namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class EventChannel<TTarget, TEvent> : IEventSender<TTarget, TEvent>
    where TEvent : IEvent
{
    private interface ISender
    {
        TTarget Target { get; }
        void Send(IEventSender<TTarget, TEvent> receiver);
    }

    private class Sender<UEvent> : ISender
        where UEvent : TEvent
    {
        public TTarget Target { get; set; }
        public UEvent Event;

        public Sender(in TTarget target, in UEvent e)
        {
            Target = target;
            Event = e;
        }

        public void Send(IEventSender<TTarget, TEvent> receiver)
            => receiver.Send(Target, Event);
    }

    private readonly record struct EventEntry(int Index, int Priority, ISender Sender);
    private readonly record struct DeferredEventEntry(IDeferrableEvent<TTarget> Event, ISender Sender);

    public int Count => (_swapTag == 0 ? _events1 : _events2).Count;

    private readonly List<EventEntry> _events1 = [];
    private readonly List<EventEntry> _events2 = [];

    private readonly List<DeferredEventEntry> _deferrableEventsToAdd1 = [];
    private readonly List<DeferredEventEntry> _deferrableEventsToAdd2 = [];

    private readonly LinkedList<DeferredEventEntry> _deferredEvents = new();

    private int _swapTag;
    private readonly object _relayLock = new();

    private static readonly Comparison<EventEntry> CompareIndexedPriority
        = (e1, e2) => {
            int pc = e1.Priority.CompareTo(e2.Priority);
            return pc == 0 ? e1.Index.CompareTo(e2.Index) : pc;
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCommandPriority(IEvent e)
        => e is ISortableEvent sortable ? sortable.Priority : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int InterlockedXor(ref int location1, int value)
    {
        int current = location1;
        while (true) {
            int newValue = current ^ value;
            int oldValue = Interlocked.CompareExchange(ref location1, newValue, current);
            if (oldValue == current) {
                return oldValue;
            }
            current = oldValue;
        }
    }

    public void Send<UEvent>(in TTarget target, in UEvent e)
        where UEvent : TEvent
    {
        var sender = new Sender<UEvent>(target, e);

        if (e is IDeferrableEvent<TTarget> deferrableEvent) {
            var eventsToAdd = _swapTag == 0 ? _deferrableEventsToAdd1 : _deferrableEventsToAdd2;
            eventsToAdd.Add(new(deferrableEvent, sender));
        }
        else {
            var receivingEvents = _swapTag == 0 ? _events1 : _events2;
            receivingEvents.Add(new(receivingEvents.Count, GetCommandPriority(e), sender));
        }
    }

    public void Relay(IEventSender<TTarget, TEvent> receiver)
    {
        lock (_relayLock) {
            DoRelay(receiver);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DoRelay(IEventSender<TTarget, TEvent> receiver)
    {
        var relayingEvents = _swapTag == 0 ? _events1 : _events2;
        var deferrableEventsToAdd = _swapTag == 0 ? _deferrableEventsToAdd1 : _deferrableEventsToAdd2;

        InterlockedXor(ref _swapTag, 1);

        var deferredEventNode = _deferredEvents.First;
        while (deferredEventNode != null) {
            var nextNode = deferredEventNode.Next;
            var (e, sender) = deferredEventNode.ValueRef;

            if (!e.ShouldDefer(sender.Target)) {
                relayingEvents.Add(
                    new(relayingEvents.Count, GetCommandPriority(e), sender));
                _deferredEvents.Remove(deferredEventNode);
            }

            deferredEventNode = nextNode;
        }

        if (deferrableEventsToAdd.Count != 0) {
            foreach (var entry in CollectionsMarshal.AsSpan(deferrableEventsToAdd)) {
                var (e, sender) = entry;
                if (e.ShouldDefer(sender.Target)) {
                    _deferredEvents.AddLast(entry);
                }
                else {
                    relayingEvents.Add(
                        new(relayingEvents.Count, GetCommandPriority(e), sender));
                }
            }
            deferrableEventsToAdd.Clear();
        }

        if (relayingEvents.Count != 0) {
            var span = CollectionsMarshal.AsSpan(relayingEvents);
            relayingEvents.Sort(CompareIndexedPriority);

            int count = 0;
            try {
                foreach (ref var entry in span) {
                    ++count;
                    entry.Sender.Send(receiver);
                }
            }
            catch {
                relayingEvents.RemoveRange(0, count);
                throw;
            }
            relayingEvents.Clear();
        }
    }
}