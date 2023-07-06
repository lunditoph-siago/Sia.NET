namespace Sia;

using System.Runtime.InteropServices;

public class Dispatcher<TTarget> : IEventSender<IEvent, TTarget>
    where TTarget : notnull
{
    public delegate bool Listener(in TTarget target, IEvent e);
    public delegate bool Listener<TEvent>(in TTarget target, TEvent e);

    private readonly Dictionary<Type, List<Listener>> _eventListeners = new();
    private readonly Dictionary<TTarget, List<Listener>> _targetListeners = new();
    private bool _sending = false;

    public Listener Listen<TEvent>(Listener listener)
        where TEvent : IEvent
        => RawListen(_eventListeners, typeof(TEvent), listener);

    public Listener ListenEx<TEvent>(Listener<TEvent> innerListener)
        where TEvent : IEvent
    {
        bool wrapperListener(in TTarget target, IEvent e) => innerListener(target, (TEvent)e);
        Listen<TEvent>(wrapperListener);
        return wrapperListener;
    }

    public Listener Listen(in TTarget target, Listener listener)
        => RawListen(_targetListeners, target, listener);

    private Listener RawListen<TKey>(Dictionary<TKey, List<Listener>> dict, TKey key, Listener listener)
        where TKey : notnull
    {
        ref var listeners = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);
        if (!exists) {
            listeners = new();
        }
        listeners!.Add(listener);
        return listener;
    }

    public bool Unlisten<TEvent>(Listener listener)
        where TEvent : IEvent
        => RawUnlisten(_eventListeners, typeof(TEvent), listener);
    
    public bool Unlisten(in TTarget target, Listener listener)
        => RawUnlisten(_targetListeners, target, listener);

    private bool RawUnlisten<TKey>(Dictionary<TKey, List<Listener>> dict, TKey key, Listener listener)
        where TKey : notnull
    {
        if (_sending) {
            throw new InvalidOperationException("Cannot do unlisten while sending");
        }
        if (!dict.TryGetValue(key, out var listeners)) {
            return false;
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

    public bool UnlistenAll<TEvent>()
        where TEvent : IEvent
        => _eventListeners.Remove(typeof(TEvent));

    public bool UnlistenAll(in TTarget target)
        => _targetListeners.Remove(target);
    
    public void Clear()
    {
        _eventListeners.Clear();
        _targetListeners.Clear();
    }

    private void ExecuteListeners(in TTarget target, List<Listener> listeners, IEvent e, int length)
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

    public void Send(in TTarget target, IEvent e)
    {
        _sending = true;

        try {
            _eventListeners.TryGetValue(e.GetType(), out var eventListeners);
            _targetListeners.TryGetValue(target, out var targetListeners);

            int eventListenerCount = eventListeners != null ? eventListeners.Count : 0;
            int targetListenerCount = targetListeners != null ? targetListeners.Count : 0;

            if (eventListenerCount != 0) {
                ExecuteListeners(target, eventListeners!, e, eventListenerCount);
            }
            if (targetListenerCount != 0) {
                ExecuteListeners(target, targetListeners!, e, targetListenerCount);
            }
        }
        finally {
            _sending = false;
            e.Dispose();
        }
    }
}

public class Dispatcher : Dispatcher<EntityRef>
{
}