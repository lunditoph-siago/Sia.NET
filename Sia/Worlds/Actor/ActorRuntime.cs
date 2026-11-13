using System.Diagnostics;

namespace Sia;

public sealed class ActorRuntime : IDisposable
{
    public static TimeSpan DefaultDisposeTimeout { get; } = TimeSpan.FromSeconds(5);

    public int WorkerCount { get; }
    public int BatchSize { get; }

    private readonly BlockingQueue<WorldActor> _readyActors = new();
    private readonly BlockingExecutor _blockingExecutor;
    private readonly object _lifecycleGate = new();
    private readonly HashSet<WorldActor> _activeActors = [];
    private bool _shutdownRequested;

#if !BROWSER
    private readonly ManualResetEventSlim _actorsDrained = new(false);
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
        _blockingExecutor = new BlockingExecutor(
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
        lock (_lifecycleGate) {
            ObjectDisposedException.ThrowIf(_shutdownRequested, this);
            var actor = new WorldActor(world, this);
            _activeActors.Add(actor);
            return actor;
        }
    }

    internal void Schedule(WorldActor actor)
    {
        _readyActors.Enqueue(actor);

#if BROWSER
        _ = Task.Run(() => {
            if (_readyActors.TryDequeue(out var readyActor)) {
                readyActor.ProcessBatch();
            }
        });
#endif
    }

    internal void OnActorCompleted(WorldActor actor)
    {
        lock (_lifecycleGate) {
            _activeActors.Remove(actor);
            StopWorkersWhenDrainedUnderLock();
        }
    }

    private void StopWorkersWhenDrainedUnderLock()
    {
        if (!_shutdownRequested || _activeActors.Count != 0) {
            return;
        }

        _readyActors.Complete();
#if !BROWSER
        _blockingExecutor.Complete();
        _actorsDrained.Set();
#endif
    }

    internal void ResumeAfterAsync(WorldActor actor, Task task)
    {
        _ = task.ContinueWith(
            static (completed, state) => {
                var (suspendedActor, runtime) = ((WorldActor, ActorRuntime))state!;
                runtime.ResumeAfterAsyncCompletion(suspendedActor, completed);
            },
            (actor, this),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ResumeAfterAsyncCompletion(WorldActor actor, Task task)
    {
        Exception? failure = null;
        try {
            task.GetAwaiter().GetResult();
        }
        catch (Exception error) {
            failure = error;
        }

        actor.ResumeFromSuspension(failure);
    }

    internal void ScheduleBlocking(
        WorldActor actor,
        IBlockingWorldMessage message,
        WorldContext context)
        => _blockingExecutor.Post(() => {
            Exception? failure = null;
            var previousActor = WorldActor.Current;
            WorldActor.Current = actor;
            try {
                message.ExecuteBlocking(context);
            }
            catch (Exception error) {
                failure = error;
            }
            finally {
                WorldActor.Current = previousActor;
            }

            actor.ResumeFromSuspension(failure);
        });

#if !BROWSER
    private void RunWorker()
    {
        while (_readyActors.Dequeue(out var actor)) {
            actor.ProcessBatch();
        }
    }
#endif

    public void Dispose()
        => Dispose(DefaultDisposeTimeout);

    public void Dispose(TimeSpan timeout)
    {
        if (WorldActor.Current?.Runtime == this) {
            throw new InvalidOperationException(
                "Cannot dispose the runtime from one of its actors.");
        }

        if (timeout != Timeout.InfiniteTimeSpan
            && (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)) {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

#if !BROWSER
        var shutdownStarted = Stopwatch.GetTimestamp();
#endif
        WorldActor[]? actorsToComplete = null;
        lock (_lifecycleGate) {
            if (!_shutdownRequested) {
                _shutdownRequested = true;
                actorsToComplete = [.. _activeActors];
                StopWorkersWhenDrainedUnderLock();
            }
        }

        // Completion takes the actor lock before entering the runtime.
        // Request it only after releasing the lifecycle lock.
        if (actorsToComplete is not null) {
            foreach (var actor in actorsToComplete) {
                actor.Complete();
            }
        }

#if !BROWSER
        if (!_actorsDrained.Wait(GetRemainingTimeout())) {
            throw new TimeoutException($"ActorRuntime actors did not drain within {timeout}.");
        }

        _blockingExecutor.WaitForCompletion(GetRemainingTimeout());
        if (!Task.WaitAll(_workers, GetRemainingTimeout())) {
            throw new TimeoutException($"ActorRuntime workers did not stop within {timeout}.");
        }

        TimeSpan GetRemainingTimeout()
        {
            if (timeout == Timeout.InfiniteTimeSpan) {
                return timeout;
            }

            var remaining = timeout - Stopwatch.GetElapsedTime(shutdownStarted);
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
