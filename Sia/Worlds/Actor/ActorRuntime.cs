using System.Diagnostics;

namespace Sia;

public sealed class ActorRuntime : IDisposable
{
    public static TimeSpan DefaultDisposeTimeout { get; } = TimeSpan.FromSeconds(5);

    public int WorkerCount { get; }
    public int BatchSize { get; }

    private readonly BlockingQueue<WorldActor> _ready = new();
    private readonly BlockingExecutor _blocking;
    private readonly object _gate = new();
    private readonly HashSet<WorldActor> _actors = [];
    private bool _closing;

#if !BROWSER
    private readonly ManualResetEventSlim _drained = new(false);
    private readonly Task[] _workers;
#endif

    public ActorRuntime(
        int workerCount = 0,
        int batchSize = 64,
        int blockingWorkerCount = 2)
    {
        WorkerCount = workerCount > 0
            ? workerCount
            : Math.Max(1, Environment.ProcessorCount - 1);
        BatchSize = Math.Max(1, batchSize);
        _blocking = new BlockingExecutor(
            Math.Max(1, blockingWorkerCount));

#if BROWSER
        // Browser drainers are spawned per schedule below.
#else
        _workers = new Task[WorkerCount];
        for (var i = 0; i < _workers.Length; i++) {
            _workers[i] = Task.Factory.StartNew(
                RunWorker,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
#endif
    }

    public WorldActor CreateActor(World world)
    {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_closing, this);
            var actor = new WorldActor(world, this);
            _actors.Add(actor);
            return actor;
        }
    }

    internal void Schedule(WorldActor actor)
    {
        _ready.Enqueue(actor);

#if BROWSER
        _ = Task.Run(() => {
            if (_ready.TryDequeue(out var ready)) ready.ProcessBatch();
        });
#endif
    }

    internal void ActorCompleted(WorldActor actor)
    {
        lock (_gate) {
            _actors.Remove(actor);
            CompleteWorkersIfDrained();
        }
    }

    private void CompleteWorkersIfDrained()
    {
        if (!_closing || _actors.Count != 0) return;
        _ready.Complete();
#if !BROWSER
        _blocking.Complete();
        _drained.Set();
#endif
    }

    internal void ContinueAsync(WorldActor actor, Task task)
    {
        _ = task.ContinueWith(
            static (completed, state) => {
                var (owner, runtime) =
                    ((WorldActor, ActorRuntime))state!;
                runtime.OnAsyncCompleted(owner, completed);
            },
            (actor, this),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnAsyncCompleted(WorldActor actor, Task task)
    {
        Exception? failure = null;
        try { task.GetAwaiter().GetResult(); }
        catch (Exception error) { failure = error; }
        actor.ResumeFromSuspension(failure);
    }

    internal void ExecuteBlocking(
        WorldActor actor,
        IBlockingWorldMessage message,
        WorldContext context)
        => _blocking.Post(() => {
            Exception? failure = null;
            var previous = WorldActor.Current;
            WorldActor.Current = actor;
            try {
                message.ExecuteBlocking(context);
            }
            catch (Exception error) { failure = error; }
            finally { WorldActor.Current = previous; }
            actor.ResumeFromSuspension(failure);
        });

#if !BROWSER
    private void RunWorker()
    {
        while (_ready.Dequeue(out var actor)) {
            actor.ProcessBatch();
        }
    }
#endif

    public void Dispose()
        => Dispose(DefaultDisposeTimeout);

    public void Dispose(TimeSpan timeout)
    {
        if (WorldActor.Current?.Runtime == this)
            throw new InvalidOperationException("Cannot dispose the runtime from one of its actors.");
        if (timeout != Timeout.InfiniteTimeSpan && (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(timeout));

#if !BROWSER
        var started = Stopwatch.GetTimestamp();
#endif
        WorldActor[]? actors = null;
        lock (_gate) {
            if (!_closing) {
                _closing = true;
                actors = [.. _actors];
                CompleteWorkersIfDrained();
            }
        }

        // Actor completion enters the runtime after taking the actor lock.
        // Request mailbox completion outside the runtime lock.
        if (actors is not null)
            foreach (var actor in actors) actor.Complete();

#if !BROWSER
        if (!_drained.Wait(Remaining()))
            throw new TimeoutException($"ActorRuntime actors did not drain within {timeout}.");
        _blocking.WaitForCompletion(Remaining());
        if (!Task.WaitAll(_workers, Remaining()))
            throw new TimeoutException($"ActorRuntime workers did not stop within {timeout}.");

        TimeSpan Remaining()
        {
            if (timeout == Timeout.InfiniteTimeSpan) return timeout;
            var remaining = timeout - Stopwatch.GetElapsedTime(started);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
#endif
    }

    private sealed class BlockingExecutor
    {
#if !BROWSER
        private readonly BlockingQueue<Action> _work = new();
        private readonly Task[] _workers;
#endif

        public BlockingExecutor(int workerCount)
        {
#if BROWSER
            // Browser blocking work is posted to the thread pool below.
#else
            _workers = new Task[workerCount];
            for (var i = 0; i < _workers.Length; i++) {
                _workers[i] = Task.Factory.StartNew(
                    RunWorker,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
#endif
        }

        public void Post(Action action)
        {
#if BROWSER
            _ = Task.Run(action);
#else
            _work.Enqueue(action);
#endif
        }

#if !BROWSER
        public void Complete() => _work.Complete();

        public void WaitForCompletion(TimeSpan timeout)
        {
            if (!Task.WaitAll(_workers, timeout)) {
                throw new TimeoutException(
                    $"ActorRuntime blocking workers did not stop within {timeout}.");
            }
        }

        private void RunWorker()
        {
            while (_work.Dequeue(out var action)) {
                action();
            }
        }
#endif
    }
}
