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
    private bool _disposing;

#if !BROWSER
    private readonly Task[] _workers;
    private int _waitingWorkers;
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
            _workers[i] = WorldActor.StartWorker(RunWorker, TaskCreationOptions.LongRunning);
        }
#endif
    }

    public WorldActor CreateActor(World world)
    {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposing, this);
            var actor = new WorldActor(world, this);
            _actors.Add(actor);
            return actor;
        }
    }

    internal void Schedule(WorldActor actor)
    {
        _ready.Enqueue(actor);

#if BROWSER
        _ = WorldActor.StartWorker(() => {
            if (_ready.TryDequeue(out var ready)) {
                ready.ProcessBatch();
            }
        }, TaskCreationOptions.DenyChildAttach);
#endif
    }

    internal void OnActorCompleted(WorldActor actor)
    {
        lock (_gate) {
            _actors.Remove(actor);
            if (_disposing && _actors.Count == 0) {
                CompleteWorkers();
            }
        }
    }

    private void CompleteWorkers()
    {
        _ready.Complete();
#if !BROWSER
        _blocking.Complete();
#endif
    }

    internal void ExecuteBlocking(
        WorldActor actor,
        IBlockingWorldMessage message,
        WorldContext context)
        => _blocking.Post(() => {
            var previous = WorldActor.BlockingActor;
            WorldActor.BlockingActor = actor;
            Exception? failure = null;
            try {
                message.ExecuteBlocking(context);
            }
            catch (Exception error) {
                failure = error;
            }
            finally {
                WorldActor.BlockingActor = previous;
            }
            actor.ResumeFromSuspension(failure);
        });

#if !BROWSER
    internal bool TryEnterWorkerWait()
    {
        lock (_gate) {
            if (_waitingWorkers == WorkerCount - 1) return false;
            _waitingWorkers++;
            return true;
        }
    }

    internal void ExitWorkerWait()
    {
        lock (_gate) {
            _waitingWorkers--;
        }
    }

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
        if (WorldActor.BlocksShutdownWait(this, null, out _)) {
            throw new InvalidOperationException("An actor cannot dispose its own runtime.");
        }
        if (timeout != Timeout.InfiniteTimeSpan
            && (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)) {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

#if !BROWSER
        var started = Stopwatch.GetTimestamp();
#endif
        WorldActor[]? actors = null;
        lock (_gate) {
            if (!_disposing) {
                _disposing = true;
                if (_actors.Count == 0) {
                    CompleteWorkers();
                }
                else {
                    actors = [.. _actors];
                }
            }
        }

        if (actors is not null) {
            foreach (var actor in actors) {
                actor.Complete();
            }
        }

#if !BROWSER
        if (!Task.WaitAll(_workers, Remaining())) {
            throw new TimeoutException(
                $"ActorRuntime workers did not stop within {timeout}.");
        }
        _blocking.WaitForCompletion(Remaining());

        TimeSpan Remaining()
        {
            if (timeout == Timeout.InfiniteTimeSpan) return timeout;
            var remaining = timeout - Stopwatch.GetElapsedTime(started);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
#endif

        GC.SuppressFinalize(this);
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
                _workers[i] = WorldActor.StartWorker(RunWorker, TaskCreationOptions.LongRunning);
            }
#endif
        }

        public void Post(Action action)
        {
#if BROWSER
            _ = WorldActor.StartWorker(action, TaskCreationOptions.DenyChildAttach);
#else
            _work.Enqueue(action);
#endif
        }

#if !BROWSER
        private void RunWorker()
        {
            while (_work.Dequeue(out var action)) {
                action();
            }
        }

        public void Complete() => _work.Complete();

        public void WaitForCompletion(TimeSpan timeout)
        {
            if (!Task.WaitAll(_workers, timeout)) {
                throw new TimeoutException(
                    $"ActorRuntime blocking workers did not stop within {timeout}.");
            }
        }
#endif
    }
}
