namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class Dispatcher<TTarget, TEvent> : IEventSender<TTarget, TEvent>
    where TTarget : notnull
    where TEvent : IEvent
{
    public delegate bool Listener<UEvent>(in TTarget target, in UEvent e)
        where UEvent : TEvent;

    private bool _sending = false;

    private readonly List<IEventListener<TTarget>> _globalListeners = new();
    private readonly Dictionary<Type, object> _eventListeners = new();
    private readonly Dictionary<TTarget, List<IEventListener<TTarget>>> _targetListeners = new();

    private readonly Stack<List<IEventListener<TTarget>>> _targetListenersPool = new();

    public void Listen(IEventListener<TTarget> listener)
    {
        _globalListeners.Add(listener);
    }

    public void Listen<UEvent>(Listener<UEvent> listener)
        where UEvent : TEvent
    {
        List<Listener<UEvent>> listeners;

        if (_eventListeners.TryGetValue(typeof(UEvent), out var rawListeners)) {
            listeners = (List<Listener<UEvent>>)rawListeners!;
        }
        else {
            listeners = new();
            _eventListeners.Add(typeof(UEvent), listeners);
        }
        
        listeners.Add(listener);
    }

    public void Listen(in TTarget target, IEventListener<TTarget> listener)
    {
        if (!_targetListeners.TryGetValue(target, out var listeners)) {
            listeners = _targetListenersPool.TryPop(out var result) ? result : new();
            _targetListeners.Add(target, listeners);
        }
        listeners.Add(listener);
    }

    public bool Unlisten(IEventListener<TTarget> listener)
    {
        GuardNotSending();
        return _globalListeners.Remove(listener);
    }

    public bool Unlisten<UEvent>(Listener<UEvent> listener)
        where UEvent : TEvent
    {
        GuardNotSending();

        if (!_eventListeners.TryGetValue(typeof(UEvent), out var rawListeners)) {
            return false;
        }

        var listeners = (List<Listener<UEvent>>)rawListeners;

        int index = listeners.IndexOf(listener);
        if (index == -1) {
            return false;
        }

        int lastIndex = listeners.Count - 1;
        if (index != lastIndex) {
            listeners[index] = listeners[lastIndex];
        }
        listeners.RemoveAt(lastIndex);
        return true;
    }
    
    public bool Unlisten(in TTarget target, IEventListener<TTarget> listener)
    {
        GuardNotSending();

        if (!_targetListeners.TryGetValue(target, out var listeners)) {
            return false;
        }

        if (listeners.Count == 1) {
            listeners.Clear();
            _targetListenersPool.Push(listeners);
            return true;
        }

        int index = listeners.IndexOf(listener);
        if (index == -1) {
            return false;
        }

        int lastIndex = listeners.Count - 1;
        if (index != lastIndex) {
            listeners[index] = listeners[lastIndex];
        }
        listeners.RemoveAt(lastIndex);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GuardNotSending()
    {
        if (_sending) {
            throw new InvalidOperationException("Cannot do this operation while the dispatcher is sending event.");
        }
    }

    public void UnlistenAll<UEvent>()
        where UEvent : TEvent
    {
        if (_eventListeners.TryGetValue(typeof(UEvent), out var rawListeners)) {
            ((List<Listener<UEvent>>)rawListeners).Clear();
        }
    }

    public void UnlistenAll(in TTarget target)
    {
        if (_targetListeners.Remove(target, out var listeners)) {
            _targetListenersPool.Push(listeners);
        }
    }
    
    public void Clear()
    {
        _globalListeners.Clear();
        _eventListeners.Clear();
        _targetListeners.Clear();
        _targetListenersPool.Clear();
    }

    public void Send<UEvent>(in TTarget target, in UEvent e)
        where UEvent : TEvent
    {
        _sending = true;

        try {
            int globalListenerCount = _globalListeners.Count;
            int eventListenerCount = 0;
            int targetListenerCount = 0;

            List<Listener<UEvent>>? eventListeners = null;
            if (_eventListeners.TryGetValue(typeof(UEvent), out var rawEventListeners)) {
                eventListeners = (List<Listener<UEvent>>)rawEventListeners;
                eventListenerCount = eventListeners.Count;
            }
            
            if (_targetListeners.TryGetValue(target, out var targetListeners)) {
                targetListenerCount = targetListeners.Count;
            }

            if (globalListenerCount != 0) {
                ExecuteListeners(target, _globalListeners, e, globalListenerCount);
            }
            if (eventListenerCount != 0) {
                ExecuteListeners(target, eventListeners!, e, eventListenerCount);
            }
            if (targetListenerCount != 0) {
                ExecuteListeners(target, targetListeners!, e, targetListenerCount);
            }
        }
        finally {
            _sending = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteListeners<UEvent>(
        in TTarget target, List<IEventListener<TTarget>> listeners, in UEvent e, int length)
        where UEvent : TEvent
    {
        int initialLength = length;

        try {
            var span = CollectionsMarshal.AsSpan(listeners);
            for (int i = 0; i < length; ++i) {
                bool exit = false;
                while (span[i].OnEvent(target, e)) {
                    span[i] = span[length - 1];
                    length--;
                    if (length <= i) {
                        exit = true;
                        break;
                    }
                }
                if (exit) { break; }
            }
        }
        finally {
            listeners.RemoveRange(length, initialLength - length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteListeners<UEvent>(
        in TTarget target, List<Listener<UEvent>> listeners, in UEvent e, int length)
        where UEvent : TEvent
    {
        int initialLength = length;

        try {
            var span = CollectionsMarshal.AsSpan(listeners);
            for (int i = 0; i < length; ++i) {
                bool exit = false;
                while (span[i](target, e)) {
                    span[i] = span[length - 1];
                    length--;
                    if (length <= i) {
                        exit = true;
                        break;
                    }
                }
                if (exit) { break; }
            }
        }
        finally {
            listeners.RemoveRange(length, initialLength - length);
        }
    }
}