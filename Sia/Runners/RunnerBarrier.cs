namespace Sia;

using Microsoft.Extensions.ObjectPool;

public sealed class RunnerBarrier
{
    private class PoolPolicy : IPooledObjectPolicy<RunnerBarrier>
    {
        public RunnerBarrier Create() => new();
        public bool Return(RunnerBarrier barrier)
        {
            var parCount = barrier.ParticipantCount;
            if (parCount != 0) {
                barrier.RemoveParticipants(parCount);
            }
#if BROWSER
            barrier._remaining = 0;
#endif
            barrier.Exception = null;
            return true;
        }
    }

    private readonly record struct CallbackEntry(Action<object?> Callback, object? UserData);

#if BROWSER
    private volatile int _remaining;
    public int ParticipantCount => _remaining;
#else
    public Barrier Raw { get; } = new(0);
    public int ParticipantCount => Raw.ParticipantCount;
#endif

    public Exception? Exception { get; private set; }

    private int _callbackCount;
    private readonly CallbackEntry[] _callbacks = new CallbackEntry[32];

    private static readonly DefaultObjectPool<RunnerBarrier> s_barrierPool
        = new(new PoolPolicy());

    public static RunnerBarrier Get() => s_barrierPool.Get();

    private RunnerBarrier() {}

    public void AddCallback(Action<object?> callback, object? userData = null)
    {
        if (_callbackCount >= 32) {
            throw new InvalidOperationException("RunnerBarrier can only have 32 callbacks");
        }
        _callbacks[_callbackCount] = new(callback, userData);
        _callbackCount++;
    }

    public void AddParticipants(int count) {
#if BROWSER
        Interlocked.Add(ref _remaining, count);
#else
        Raw.AddParticipants(count);
#endif
    }

    public void RemoveParticipants(int count) {
#if BROWSER
        Interlocked.Add(ref _remaining, -count);
#else
        Raw.RemoveParticipants(count);
#endif
    }

    public void Signal() {
#if BROWSER
        Interlocked.Decrement(ref _remaining);
#else
        RemoveParticipants(1);
#endif
    }

    public void Throw(Exception e)
    {
        Exception = e;
        RemoveParticipants(1);
    }

    public void Wait()
    {
#if BROWSER
        var spinner = new SpinWait();
 
        while (Volatile.Read(ref _remaining) > 0) {
            spinner.SpinOnce();
        }
#else
        AddParticipants(1);
        Raw.SignalAndWait();
#endif

        Exception? exception;

        try {
            for (var i = 0; i != _callbackCount; ++i) {
                ref var entry = ref _callbacks[i];
                entry.Callback(entry.UserData);
            }
        }
        finally {
            Array.Clear(_callbacks, 0, _callbackCount);
            _callbackCount = 0;
            exception = Exception;
        }

        if (exception != null) {
            throw exception;
        }
    }

    public void WaitAndReturn()
    {
        try {
            Wait();
        }
        finally {
            s_barrierPool.Return(this);
        }
    }
}