namespace Sia;

using System.Runtime.CompilerServices;

public abstract class Dispatcher<TTarget, TKey, TEvent> : IEventSender<TTarget, TEvent>
    where TKey : notnull
    where TEvent : IEvent
{
    public delegate bool Listener<UEvent>(TTarget target, in UEvent e)
        where UEvent : TEvent;

    private bool _sending = false;

    private readonly List<IEventListener<TTarget>> _globalListeners = [];
    private ArrayBuffer<object> _eventListeners = new();
    private readonly Dictionary<TKey, List<IEventListener<TTarget>>> _targetListeners = [];

    private readonly Stack<List<IEventListener<TTarget>>> _targetListenersPool = new();

    protected abstract TKey GetKey(TTarget target);

    public void Listen(IEventListener<TTarget> listener)
    {
        _globalListeners.Add(listener);
    }

    public void Listen<UEvent>(Listener<UEvent> listener)
        where UEvent : TEvent
    {
        int typeIndex = EventTypeIndexer<UEvent>.Index;

        List<Listener<UEvent>> listeners;
        ref var rawListeners = ref _eventListeners.GetRefOrNullRef(typeIndex);
        
        if (Unsafe.IsNullRef(ref rawListeners) || rawListeners == null) {
            listeners = [];
            int count = _eventListeners.Count;
            if (count <= typeIndex) {
                _eventListeners.Count = typeIndex + 1;
            }
            _eventListeners.GetRef(typeIndex) = listeners;
        }
        else {
            listeners = (List<Listener<UEvent>>)rawListeners;
        }

        listeners.Add(listener);
    }

    public void Listen(TTarget target, IEventListener<TTarget> listener)
    {
        var key = GetKey(target);
        if (!_targetListeners.TryGetValue(key, out var listeners)) {
            listeners = _targetListenersPool.TryPop(out var result) ? result : [];
            _targetListeners.Add(key, listeners);
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

        int typeIndex = EventTypeIndexer<UEvent>.Index;
        ref var rawListeners = ref _eventListeners.GetRefOrNullRef(typeIndex);

        if (Unsafe.IsNullRef(ref rawListeners) || rawListeners == null) {
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
    
    public bool Unlisten(TTarget target, IEventListener<TTarget> listener)
    {
        GuardNotSending();

        if (!_targetListeners.TryGetValue(GetKey(target), out var listeners)) {
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

    public void UnlistenAll<UEvent>()
        where UEvent : TEvent
    {
        GuardNotSending();

        int typeIndex = EventTypeIndexer<UEvent>.Index;
        ref var rawListeners = ref _eventListeners.GetRefOrNullRef(typeIndex);
        if (Unsafe.IsNullRef(ref rawListeners) || rawListeners == null) {
            return;
        }
        var listeners = (List<Listener<UEvent>>)rawListeners;
        listeners.Clear();
    }

    public void UnlistenAll(TTarget target)
    {
        GuardNotSending();

        if (_targetListeners.Remove(GetKey(target), out var listeners)) {
            _targetListenersPool.Push(listeners);
        }
    }
    
    public void Clear()
    {
        GuardNotSending();

        _globalListeners.Clear();
        _eventListeners.Count = 0;
        _targetListeners.Clear();
        _targetListenersPool.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GuardNotSending()
    {
        if (_sending) {
            throw new InvalidOperationException("This operation is not allowed while sending event.");
        }
    }

    public void Send<UEvent>(TTarget target, in UEvent e)
        where UEvent : TEvent
    {
        _sending = true;

        int typeIndex = EventTypeIndexer<UEvent>.Index;

        try {
            int globalListenerCount = _globalListeners.Count;
            int eventListenerCount = 0;
            int targetListenerCount = 0;

            List<Listener<UEvent>>? eventListeners = null;
            ref var rawEventListeners = ref _eventListeners.GetRefOrNullRef(typeIndex);

            if (!Unsafe.IsNullRef(ref rawEventListeners) && rawEventListeners != null) {
                eventListeners = (List<Listener<UEvent>>)rawEventListeners;
                eventListenerCount = eventListeners.Count;
            }
            
            if (_targetListeners.TryGetValue(GetKey(target), out var targetListeners)) {
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
        TTarget target, List<IEventListener<TTarget>> listeners, in UEvent e, int length)
        where UEvent : TEvent
    {
        int initialLength = length;

        try {
            for (int i = 0; i < length; ++i) {
                bool exit = false;
                while (listeners[i].OnEvent(target, e)) {
                    listeners[i] = listeners[length - 1];
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
            if (initialLength != length) {
                listeners.RemoveRange(length, initialLength - length);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExecuteListeners<UEvent>(
        TTarget target, List<Listener<UEvent>> listeners, in UEvent e, int length)
        where UEvent : TEvent
    {
        int initialLength = length;

        try {
            for (int i = 0; i < length; ++i) {
                bool exit = false;
                while (listeners[i](target, e)) {
                    listeners[i] = listeners[length - 1];
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

public class Dispatcher<TTarget, TEvent> : Dispatcher<TTarget, TTarget, TEvent>
    where TTarget : notnull
    where TEvent : IEvent
{
    protected override TTarget GetKey(TTarget target) => target;
}