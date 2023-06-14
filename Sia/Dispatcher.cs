namespace Sia;

using System.Runtime.InteropServices;

public class Dispatcher<TTarget>
    where TTarget : notnull
{
    public delegate bool Listener(TTarget target, ICommand command);
    public delegate bool Listener<TCommand>(TTarget target, TCommand command);

    private Dictionary<Type, List<Listener>> _commandListeners = new();
    private Dictionary<TTarget, List<Listener>> _targetListeners = new();
    private bool _sending = false;

    public Listener Listen<TCommand>(Listener listener)
        where TCommand : ICommand
        => RawListen(_commandListeners, typeof(TCommand), listener);

    public Listener ListenEx<TCommand>(Listener<TCommand> innerListener)
        where TCommand : ICommand
    {
        Listener wrapperListener = (target, cmd) => innerListener(target, (TCommand)cmd);
        Listen<TCommand>(wrapperListener);
        return wrapperListener;
    }

    public Listener Listen(TTarget target, Listener listener)
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

    public bool Unlisten<TCommand>(Listener listener)
        where TCommand : ICommand
        => RawUnlisten(_commandListeners, typeof(TCommand), listener);
    
    public bool Unlisten(TTarget target, Listener listener)
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
        int lastIndex = listeners.Count();
        if (index != lastIndex) {
            listeners[index] = listeners[lastIndex];
        }
        listeners.RemoveAt(lastIndex);
        return true;
    }

    public bool UnlistenAll<TCommand>()
        where TCommand : ICommand
        => _commandListeners.Remove(typeof(TCommand));

    public bool UnlistenAll(TTarget target)
        => _targetListeners.Remove(target);
    
    public void Clear()
    {
        _commandListeners.Clear();
        _targetListeners.Clear();
    }

    private void ExecuteListeners(TTarget target, List<Listener> listeners, ICommand command)
    {
        var span = CollectionsMarshal.AsSpan(listeners);
        int lastIndex = span.Length - 1;

        try {
            for (int i = 0; i <= lastIndex; ++i) {
                bool exit = false;
                while (span[i](target, command)) {
                    span[i] = span[lastIndex];
                    lastIndex--;
                    if (lastIndex < i) {
                        exit = true;
                        break;
                    }
                }
                if (exit) { break; }
            }
        }
        finally {
            listeners.RemoveRange(lastIndex + 1, span.Length - lastIndex - 1);
        }
    }

    public void Send<TCommand>(TTarget target, TCommand command)
        where TCommand : ICommand
    {
        _sending = true;

        try {
            if (_commandListeners.TryGetValue(typeof(TCommand), out var commandListeners)) {
                ExecuteListeners(target, commandListeners, command);
            }
            if (_targetListeners.TryGetValue(target, out var targetListeners)) {
                ExecuteListeners(target, targetListeners, command);
            }
        }
        finally {
            _sending = false;
            command.Dispose();
        }
    }
}

public class Dispatcher : Dispatcher<EntityRef>
{
}