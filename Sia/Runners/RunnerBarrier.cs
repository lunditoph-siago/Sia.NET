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
#if BROWSER
            barrier.ResetTcs();
#endif
            return true;
        }
    }

    private readonly record struct CallbackEntry(Action<object?> Callback, object? UserData);

#if BROWSER
    private int _participantCount;
    private int _remaining;
    private TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int ParticipantCount => Volatile.Read(ref _participantCount);
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

    public void AddParticipants(int count)
    {
#if BROWSER
        Interlocked.Add(ref _participantCount, count);
        Interlocked.Add(ref _remaining, count); 
#else
        Raw.AddParticipants(count);
#endif
    }

    public void RemoveParticipants(int count)
    {
#if BROWSER
        Interlocked.Add(ref _participantCount, -count);
        if (Interlocked.Add(ref _remaining, -count) == 0) {
            _tcs.TrySetResult(true);
        }
#else
        Raw.RemoveParticipants(count);
#endif
    }

    public void Signal() => RemoveParticipants(1);

    public void SignalAndWait()
    {
#if BROWSER
        var remaining = Interlocked.Decrement(ref _remaining);
        Interlocked.Decrement(ref _participantCount);
        if (remaining == 0) {
            _tcs.TrySetResult(true);
        } else if (remaining < 0) {
            throw new InvalidOperationException("Barrier already completed or participant count exceeded");
        }
        _tcs.Task.Wait();
#else
        Raw.SignalAndWait();
#endif
    }

    public void Throw(Exception e)
    {
        Exception = e;
        RemoveParticipants(1);
    }

    public void Wait()
    {
        AddParticipants(1);
        SignalAndWait();

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

#if BROWSER
    internal void ResetTcs() {
        _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _participantCount = 0;
        _remaining = 0;
    }
#endif
}