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
            barrier.Exception = null;
            return true;
        }
    }

    private readonly record struct CallbackEntry(Action<object?> Callback, object? UserData);

    public int ParticipantCount => Raw.ParticipantCount;

    public Barrier Raw { get; } = new(0);
    public Exception? Exception { get; private set; }

    private int _callbackCount;
    private readonly CallbackEntry[] _callbacks = new CallbackEntry[32];

    private static readonly DefaultObjectPool<RunnerBarrier> s_barrierPool
        = new(new PoolPolicy());

    public static RunnerBarrier Get() => s_barrierPool.Get();

    private RunnerBarrier() { }

    public void AddCallback(Action<object?> callback, object? userData = null)
    {
        if (_callbackCount >= 32) {
            throw new InvalidOperationException("RunnerBarrier can only have 32 callbacks");
        }
        _callbacks[_callbackCount] = new(callback, userData);
        _callbackCount++;
    }

    public void AddParticipants(int count) => Raw.AddParticipants(count);
    public void RemoveParticipants(int count) => Raw.RemoveParticipants(count);

    public void Signal() => Raw.RemoveParticipant();

    public void Throw(Exception e)
    {
        Exception = e;
        Raw.RemoveParticipant();
    }

    public void Wait()
    {
        Raw.AddParticipant();
        Raw.SignalAndWait();

        Exception? exception;

        try {
            for (int i = 0; i != _callbackCount; ++i) {
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