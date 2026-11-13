using System.Runtime.CompilerServices;

namespace Sia;

public abstract class Dispatcher<TTarget, TKey, TEvent> : IEventSender<TTarget, TEvent>
    where TKey : notnull
    where TEvent : IEvent
{
    public delegate bool Listener<UEvent>(TTarget target, in UEvent e)
        where UEvent : TEvent;

    private interface IListenerList
    {
        void Compact();
    }

    private class ListenerList<T> : List<T?>, IListenerList where T : class
    {
        public bool NeedsCompaction { get; set; }

        public void Compact()
        {
            RemoveAll(static listener => listener is null);
            NeedsCompaction = false;
        }
    }

    private sealed class TargetListenerList : ListenerList<IEventListener<TTarget>>
    {
        public TKey Key { get; set; } = default!;
    }

    private int _sendDepth;
    private List<IListenerList>? _pendingListenerCompactions;
    private List<Action>? _afterSendActions;
    private int _afterSendActionIndex;
    private bool _flushingAfterSendActions;

    public bool IsSending => _sendDepth != 0;

    public int PendingAfterSendActionCount => (_afterSendActions?.Count ?? 0) - _afterSendActionIndex;

    private readonly ListenerList<IEventListener<TTarget>> _globalListeners = [];
    private readonly ArrayBuffer<object> _typedListeners = new();
    private readonly Dictionary<TKey, TargetListenerList> _targetListeners = [];
    private readonly Stack<TargetListenerList> _targetListenersPool = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasNoListeners<UEvent>() where UEvent : TEvent
    {
        if (_globalListeners.Count != 0 || _targetListeners.Count != 0) {
            return false;
        }

        return GetTypedListeners<UEvent>(EventTypeIndexer<UEvent>.Index) is not { Count: > 0 };
    }

    protected abstract TKey GetKey(TTarget target);

    public void Listen(IEventListener<TTarget> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _globalListeners.Add(listener);
    }

    public void Listen<UEvent>(Listener<UEvent> listener)
        where UEvent : TEvent
    {
        ArgumentNullException.ThrowIfNull(listener);
        var typeIndex = EventTypeIndexer<UEvent>.Index;

        var listeners = GetTypedListeners<UEvent>(typeIndex);
        if (listeners is null) {
            listeners = [];
            if (_typedListeners.Count <= typeIndex) {
                _typedListeners.Count = typeIndex + 1;
            }
            _typedListeners.GetRef(typeIndex) = listeners;
        }

        listeners.Add(listener);
    }

    public void Listen(TTarget target, IEventListener<TTarget> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        var key = GetKey(target);
        if (!_targetListeners.TryGetValue(key, out var listeners)) {
            listeners = _targetListenersPool.TryPop(out var result) ? result : [];
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
        if (index < 0) {
            return false;
        }
        if (IsSending) {
            MarkListenerForRemoval(_globalListeners, index);
        }
        else {
            _globalListeners.RemoveAt(index);
        }
        return true;
    }

    internal bool RetireListener<UEvent>(Listener<UEvent> listener)
        where UEvent : TEvent
    {
        if (GetTypedListeners<UEvent>(EventTypeIndexer<UEvent>.Index) is not { } listeners) {
            return false;
        }
        var index = listeners.IndexOf(listener);
        if (index < 0) {
            return false;
        }

        RemoveUnorderedListener(listeners, index);
        return true;
    }

    internal bool RetireListener(TTarget target, IEventListener<TTarget> listener)
    {
        var key = GetKey(target);
        if (!_targetListeners.TryGetValue(key, out var listeners)) {
            return false;
        }

        var index = listeners.IndexOf(listener);
        if (index < 0) {
            return false;
        }
        RemoveUnorderedListener(listeners, index);
        if (listeners.Count == 0) {
            _targetListeners.Remove(key);
            RecycleTargetListeners(listeners);
        }
        return true;
    }

    private void RemoveUnorderedListener<T>(ListenerList<T> listeners, int index)
        where T : class
    {
        if (IsSending) {
            MarkListenerForRemoval(listeners, index);
            return;
        }
        // Immediate typed and target removal use swap-back ordering.
        var lastIndex = listeners.Count - 1;
        if (index != lastIndex) {
            listeners[index] = listeners[lastIndex];
        }
        listeners.RemoveAt(lastIndex);
    }

    public void UnlistenAll<UEvent>()
        where UEvent : TEvent
    {
        GuardNotSending();

        GetTypedListeners<UEvent>(EventTypeIndexer<UEvent>.Index)?.Clear();
    }

    public virtual void UnlistenAll(TTarget target)
    {
        GuardNotSending();

        if (_targetListeners.Remove(GetKey(target), out var listeners)) {
            RecycleTargetListeners(listeners);
        }
    }

    private void RecycleTargetListeners(TargetListenerList listeners)
    {
        listeners.Clear();
        listeners.Key = default!;
        _targetListenersPool.Push(listeners);
    }

    public virtual void Clear()
    {
        GuardNotSending();

        _globalListeners.Clear();
        _typedListeners.AsSpan()[.._typedListeners.Count].Clear();
        _typedListeners.Count = 0;
        _targetListeners.Clear();
        _targetListenersPool.Clear();
    }

    internal bool HasNoUntypedListeners => _globalListeners.Count == 0 && _targetListeners.Count == 0;

    internal bool HasNoTypedListeners<UEvent>() where UEvent : TEvent
    {
        if (!EventTypeIndexState<UEvent>.TryGetIndex(out var index)) {
            return true;
        }

        return GetTypedListeners<UEvent>(index) is not { Count: > 0 };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ListenerList<Listener<UEvent>>? GetTypedListeners<UEvent>(int index)
        where UEvent : TEvent
    {
        ref var listeners = ref _typedListeners.GetRefOrNullRef(index);
        return Unsafe.IsNullRef(ref listeners)
            ? null
            : Unsafe.As<ListenerList<Listener<UEvent>>>(listeners);
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
        if (!_flushingAfterSendActions && _afterSendActions is { Count: > 0 } actions) {
            DrainAfterSendActions(actions);
        }
    }

    private void DrainAfterSendActions(List<Action> actions)
    {
        var result = Outcome<Exception>.Success;
        _flushingAfterSendActions = true;
        try {
            while (_afterSendActionIndex < actions.Count) {
                result = result.Attempt(actions[_afterSendActionIndex++]);
            }
        }
        finally {
            actions.Clear();
            _afterSendActionIndex = 0;
            _flushingAfterSendActions = false;
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
        => SendCore(target, e, null);

    protected void SendCore<UEvent>(TTarget target, in UEvent e, IEventSink<TTarget, UEvent>? owner)
        where UEvent : TEvent
    {
        var globalListeners = _globalListeners;
        var globalListenerCount = globalListeners.Count;

        ListenerList<Listener<UEvent>>? eventListeners = null;
        var eventListenerCount = 0;
        if (_typedListeners.Count != 0) {
            ref var rawEventListeners = ref _typedListeners.GetRefOrNullRef(
                EventTypeIndexer<UEvent>.Index);
            if (!Unsafe.IsNullRef(ref rawEventListeners) && rawEventListeners is not null) {
                eventListeners = Unsafe.As<ListenerList<Listener<UEvent>>>(rawEventListeners);
                eventListenerCount = eventListeners.Count;
            }
        }

        TargetListenerList? targetListeners = null;
        var targetListenerCount = 0;
        if (_targetListeners.Count != 0
            && _targetListeners.TryGetValue(GetKey(target), out targetListeners)) {
            targetListenerCount = targetListeners.Count;
        }

        if ((globalListenerCount | eventListenerCount | targetListenerCount) == 0 && owner is null) {
            return;
        }

        // Each group keeps its original length during this send.
        _sendDepth++;
        try {
            if (globalListenerCount != 0) {
                ExecuteListeners(target, globalListeners, e, globalListenerCount);
            }
            if (eventListenerCount != 0) {
                ExecuteListeners(target, eventListeners!, e, eventListenerCount);
            }
            owner?.OnEvent(target, e);
            if (targetListenerCount != 0) {
                ExecuteListeners(target, targetListeners!, e, targetListenerCount);
            }
        }
        catch (Exception error) {
            _sendDepth--;
            if (_sendDepth == 0) {
                CompactListenerLists();
                FlushFailedSendActions(error);
            }
            throw;
        }
        _sendDepth--;
        if (_sendDepth == 0) {
            CompactListenerLists();
            FlushAfterSendActions();
        }
    }

    // Keep the listener failure first and report the deferred work as one cleanup.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FlushFailedSendActions(Exception dispatchError)
        => Outcome<Exception>.Failure(dispatchError)
            .Attempt(this, static (in Dispatcher<TTarget, TKey, TEvent> dispatcher) =>
                dispatcher.FlushAfterSendActions())
            .ThrowIfFailed();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteListeners<UEvent>(
        TTarget target, ListenerList<IEventListener<TTarget>> listeners, in UEvent e, int length)
        where UEvent : TEvent
    {
        for (var i = 0; i < length; ++i) {
            if (listeners[i] is { } listener && listener.OnEvent(target, e)) {
                MarkListenerForRemoval(listeners, i);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteListeners<UEvent>(
        TTarget target, ListenerList<Listener<UEvent>> listeners, in UEvent e, int length)
        where UEvent : TEvent
    {
        for (var i = 0; i < length; ++i) {
            if (listeners[i] is { } listener && listener(target, e)) {
                MarkListenerForRemoval(listeners, i);
            }
        }
    }

    private void MarkListenerForRemoval<T>(ListenerList<T> listeners, int index)
        where T : class
    {
        // Keep the indices captured by every active send valid.
        listeners[index] = null;
        if (!listeners.NeedsCompaction) {
            (_pendingListenerCompactions ??= []).Add(listeners);
            listeners.NeedsCompaction = true;
        }
    }

    private void CompactListenerLists()
    {
        if (_pendingListenerCompactions is not { Count: > 0 } lists) {
            return;
        }
        foreach (var listeners in lists) {
            listeners.Compact();
            if (listeners is TargetListenerList { Count: 0 } targetListeners) {
                _targetListeners.Remove(targetListeners.Key);
                RecycleTargetListeners(targetListeners);
            }
        }
        lists.Clear();
    }
}

public class Dispatcher<TTarget, TEvent> : Dispatcher<TTarget, TTarget, TEvent>
    where TTarget : notnull
    where TEvent : IEvent
{
    protected override TTarget GetKey(TTarget target) => target;
}
