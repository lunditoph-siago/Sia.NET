using System.Runtime.CompilerServices;

namespace Sia;

public abstract class Dispatcher<TTarget, TKey, TEvent> : IEventSender<TTarget, TEvent>
    where TKey : notnull
    where TEvent : IEvent
{
    public delegate bool Listener<UEvent>(TTarget target, in UEvent e)
        where UEvent : TEvent;

    private sealed class ListenerList : List<object?>
    {
        public TKey Key { get; set; } = default!;
        public bool Targeted { get; init; }
        public bool NeedsCompaction { get; set; }
    }

    private int _sendDepth;
    private List<ListenerList>? _pendingListenerCompactions;
    private List<Action>? _afterSendActions;
    private int _afterSendActionIndex;
    private bool _flushingAfterSendActions;

    public bool IsSending => _sendDepth != 0;

    public int PendingAfterSendActionCount => (_afterSendActions?.Count ?? 0) - _afterSendActionIndex;

    private readonly ListenerList _globalListeners = [];
    private readonly ArrayBuffer<object> _eventListeners = new();
    private readonly Dictionary<TKey, ListenerList> _targetListeners = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasNoListeners<UEvent>() where UEvent : TEvent
    {
        if (_globalListeners.Count != 0 || _targetListeners.Count != 0) return false;
        ref var listeners = ref _eventListeners.GetRefOrNullRef(EventTypeIndexer<UEvent>.Index);
        return Unsafe.IsNullRef(ref listeners) || listeners is null
            || Unsafe.As<ListenerList>(listeners).Count == 0;
    }

    private readonly Stack<ListenerList> _targetListenersPool = new();

    protected abstract TKey GetKey(TTarget target);

    public void Listen(IEventListener<TTarget> listener)
    {
        _globalListeners.Add(listener);
    }

    public void Listen<UEvent>(Listener<UEvent> listener)
        where UEvent : TEvent
    {
        var typeIndex = EventTypeIndexer<UEvent>.Index;

        ListenerList listeners;
        ref var rawListeners = ref _eventListeners.GetRefOrNullRef(typeIndex);

        if (Unsafe.IsNullRef(ref rawListeners) || rawListeners == null) {
            listeners = [];
            var count = _eventListeners.Count;
            if (count <= typeIndex) {
                _eventListeners.Count = typeIndex + 1;
            }
            _eventListeners.GetRef(typeIndex) = listeners;
        }
        else {
            listeners = Unsafe.As<ListenerList>(rawListeners);
        }

        listeners.Add(listener);
    }

    public void Listen(TTarget target, IEventListener<TTarget> listener)
    {
        var key = GetKey(target);
        if (!_targetListeners.TryGetValue(key, out var listeners)) {
            listeners = _targetListenersPool.TryPop(out var result) ? result : new() { Targeted = true };
            listeners.Key = key;
            _targetListeners.Add(key, listeners);
        }
        listeners.Add(listener);
    }

    public bool Unlisten(IEventListener<TTarget> listener)
    {
        GuardNotSending();
        return RetireListener(listener);
    }

    public bool Unlisten<UEvent>(Listener<UEvent> listener)
        where UEvent : TEvent
    {
        GuardNotSending();
        return RetireListener(listener);
    }

    public bool Unlisten(TTarget target, IEventListener<TTarget> listener)
    {
        GuardNotSending();
        return RetireListener(target, listener);
    }

    internal bool RetireListener(IEventListener<TTarget> listener)
    {
        var index = _globalListeners.IndexOf(listener);
        if (index < 0) return false;
        if (IsSending) {
            RemoveListener(_globalListeners, index, _globalListeners.Count, deferred: true);
        }
        else {
            _globalListeners.RemoveAt(index);
        }
        return true;
    }

    internal bool RetireListener<UEvent>(Listener<UEvent> listener)
        where UEvent : TEvent
    {
        var typeIndex = EventTypeIndexer<UEvent>.Index;
        ref var rawListeners = ref _eventListeners.GetRefOrNullRef(typeIndex);

        if (Unsafe.IsNullRef(ref rawListeners) || rawListeners == null) {
            return false;
        }

        var listeners = Unsafe.As<ListenerList>(rawListeners);
        var index = listeners.IndexOf(listener);

        if (index == -1) {
            return false;
        }

        RemoveListener(listeners, index, listeners.Count, deferred: IsSending);
        return true;
    }

    internal bool RetireListener(TTarget target, IEventListener<TTarget> listener)
    {
        var key = GetKey(target);
        if (!_targetListeners.TryGetValue(key, out var listeners)) {
            return false;
        }

        if (!IsSending && listeners.Count == 1) {
            if (!Equals(listeners[0], listener)) {
                return false;
            }
            _targetListeners.Remove(key);
            listeners.Clear();
            listeners.Key = default!;
            _targetListenersPool.Push(listeners);
            return true;
        }

        var index = listeners.IndexOf(listener);
        if (index == -1) {
            return false;
        }

        RemoveListener(listeners, index, listeners.Count, deferred: IsSending);
        return true;
    }

    public void UnlistenAll<UEvent>()
        where UEvent : TEvent
    {
        GuardNotSending();

        var typeIndex = EventTypeIndexer<UEvent>.Index;
        ref var rawListeners = ref _eventListeners.GetRefOrNullRef(typeIndex);
        if (Unsafe.IsNullRef(ref rawListeners) || rawListeners == null) {
            return;
        }
        var listeners = Unsafe.As<ListenerList>(rawListeners);
        listeners.Clear();
    }

    public virtual void UnlistenAll(TTarget target)
    {
        GuardNotSending();

        if (_targetListeners.Remove(GetKey(target), out var listeners)) {
            listeners.Clear();
            listeners.Key = default!;
            _targetListenersPool.Push(listeners);
        }
    }

    public virtual void Clear()
    {
        GuardNotSending();

        _globalListeners.Clear();
        _eventListeners.AsSpan()[.._eventListeners.Count].Clear();
        _eventListeners.Count = 0;
        _targetListeners.Clear();
        _targetListenersPool.Clear();
    }

    internal bool HasNoUntypedListeners => _globalListeners.Count == 0 && _targetListeners.Count == 0;

    internal bool HasNoTypedListeners<UEvent>() where UEvent : TEvent
    {
        if (!EventTypeIndexState<UEvent>.TryGetIndex(out var index)) return true;
        ref var raw = ref _eventListeners.GetRefOrNullRef(index);
        return Unsafe.IsNullRef(ref raw) || raw == null || Unsafe.As<ListenerList>(raw).Count == 0;
    }

    public void RunAfterSend(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_sendDepth == 0 && !_flushingAfterSendActions) {
            action();
            return;
        }
        (_afterSendActions ??= []).Add(action);
    }

    private void FlushAfterSendActions()
    {
        if (_flushingAfterSendActions || _afterSendActions is not { Count: > 0 } actions) {
            return;
        }
        FlushAfterSendActions(actions);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CompleteSend(Exception? error = null)
        => (error is null ? Outcome<Exception>.Success : Outcome<Exception>.Failure(error))
            .Attempt(this, static (in Dispatcher<TTarget, TKey, TEvent> dispatcher) => dispatcher.CompactListenerLists())
            .Attempt(this, static (in Dispatcher<TTarget, TKey, TEvent> dispatcher) => dispatcher.FlushAfterSendActions())
            .ThrowIfFailed();

    private void FlushAfterSendActions(List<Action> actions)
    {
        _flushingAfterSendActions = true;
        try {
            while (_afterSendActionIndex < actions.Count) {
                var action = actions[_afterSendActionIndex++];
                action();
            }
        }
        catch (Exception error) {
            FlushAfterSendActions(actions, error);
        }
        finally {
            actions.Clear();
            _afterSendActionIndex = 0;
            _flushingAfterSendActions = false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FlushAfterSendActions(List<Action> actions, Exception error)
    {
        var result = Outcome<Exception>.Failure(error);
        while (_afterSendActionIndex < actions.Count) {
            result = result.Attempt(actions[_afterSendActionIndex++]);
        }
        result.ThrowIfFailed();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void GuardNotSending()
    {
        if (_sendDepth != 0) {
            throw new InvalidOperationException("This operation is not allowed while sending event.");
        }
    }

    public virtual void Send<UEvent>(TTarget target, in UEvent e)
        where UEvent : TEvent
        => SendToListeners(target, e, null);

    protected void SendToListeners<UEvent>(TTarget target, in UEvent e, IEventSink<TTarget, UEvent>? owner)
        where UEvent : TEvent
    {
        var globalListeners = _globalListeners;
        var globalListenerCount = globalListeners.Count;

        ListenerList? eventListeners = null;
        var eventListenerCount = 0;
        if (_eventListeners.Count != 0) {
            ref var rawEventListeners = ref _eventListeners.GetRefOrNullRef(
                EventTypeIndexer<UEvent>.Index);
            if (!Unsafe.IsNullRef(ref rawEventListeners) && rawEventListeners != null) {
                eventListeners = Unsafe.As<ListenerList>(rawEventListeners);
                eventListenerCount = eventListeners.Count;
            }
        }

        ListenerList? targetListeners = null;
        var targetListenerCount = 0;
        if (_targetListeners.Count != 0
            && _targetListeners.TryGetValue(GetKey(target), out targetListeners)) {
            targetListenerCount = targetListeners.Count;
        }

        if ((globalListenerCount | eventListenerCount | targetListenerCount) == 0 && owner is null) {
            return;
        }

        _sendDepth++;
        try {
            if (globalListenerCount != 0) {
                ExecuteListeners(target, globalListeners, e, globalListenerCount);
            }
            if (eventListenerCount != 0) {
                ExecuteTypedListeners(target, eventListeners!, e, eventListenerCount);
            }
            owner?.OnEvent(target, e);
            if (targetListenerCount != 0) {
                ExecuteListeners(target, targetListeners!, e, targetListenerCount);
            }
        }
        catch (Exception error) {
            _sendDepth--;
            if (_sendDepth == 0) {
                CompleteSend(error);
            }
            throw;
        }
        _sendDepth--;
        if (_sendDepth == 0) {
            if (_pendingListenerCompactions is { Count: > 0 }) {
                CompleteSend();
            }
            else {
                FlushAfterSendActions();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteListeners<UEvent>(
        TTarget target, ListenerList listeners, in UEvent e, int length)
        where UEvent : TEvent
    {
        for (var i = 0; i < length; ++i) {
            if (listeners[i] is { } listener && Unsafe.As<IEventListener<TTarget>>(listener).OnEvent(target, e)
                && RemoveListener(listeners, i, length, deferred: _sendDepth != 1)) {
                length--;
                i--;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteTypedListeners<UEvent>(
        TTarget target, ListenerList listeners, in UEvent e, int length)
        where UEvent : TEvent
    {
        for (var i = 0; i < length; ++i) {
            if (listeners[i] is { } listener && Unsafe.As<Listener<UEvent>>(listener)(target, e)
                && RemoveListener(listeners, i, length, deferred: _sendDepth != 1)) {
                length--;
                i--;
            }
        }
    }

    private bool RemoveListener(ListenerList listeners, int index, int length, bool deferred)
    {
        if (!deferred && !listeners.NeedsCompaction) {
            var lastIndex = length - 1;
            if (index != lastIndex) listeners[index] = listeners[lastIndex];
            listeners.RemoveAt(lastIndex);
            if (listeners.Targeted && listeners.Count == 0) {
                _targetListeners.Remove(listeners.Key);
                listeners.Key = default!;
                _targetListenersPool.Push(listeners);
            }
            return true;
        }

        listeners[index] = null;
        if (!listeners.NeedsCompaction) {
            (_pendingListenerCompactions ??= []).Add(listeners);
            listeners.NeedsCompaction = true;
        }
        return false;
    }

    private void CompactListenerLists()
    {
        if (_pendingListenerCompactions is not { Count: > 0 } lists) return;
        var result = Outcome<Exception>.Success;
        try {
            foreach (var listeners in lists) {
                try {
                    listeners.RemoveAll(static listener => listener is null);
                    listeners.NeedsCompaction = false;
                    if (listeners.Targeted && listeners.Count == 0) {
                        _targetListeners.Remove(listeners.Key);
                        listeners.Key = default!;
                        _targetListenersPool.Push(listeners);
                    }
                }
                catch (Exception error) {
                    result = result.Combine(Outcome<Exception>.Failure(error));
                }
            }
        }
        finally {
            lists.Clear();
        }
        result.ThrowIfFailed();
    }
}

public class Dispatcher<TTarget, TEvent> : Dispatcher<TTarget, TTarget, TEvent>
    where TTarget : notnull
    where TEvent : IEvent
{
    protected override TTarget GetKey(TTarget target) => target;
}
