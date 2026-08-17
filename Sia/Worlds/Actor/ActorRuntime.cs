namespace Sia;

using System.Collections.Concurrent;

public sealed class ActorRuntime : IDisposable
{
    public int WorkerCount { get; }
    public int BatchSize { get; }

    private readonly BlockingQueue<WorldActor> _ready = new();
    private readonly BlockingExecutor _blocking;
    private int _disposed;
    private int _drainerIds;

#if !BROWSER
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
        => new(world, this);

    internal void Schedule(WorldActor actor)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        _ready.Enqueue(actor);

#if BROWSER
        var workerId = Interlocked.Increment(ref _drainerIds);
        _ = Task.Run(() => RunWorker(workerId));
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
        => actor.ResumeFromSuspension(
            task.IsFaulted ? task.Exception : null);

    internal void ExecuteBlocking(
        WorldActor actor,
        IBlockingWorldMessage message,
        WorldContext context)
        => _blocking.Post(() => {
            try {
                message.ExecuteBlocking(context);
                actor.ResumeFromSuspension();
            }
            catch (Exception error) {
                actor.ResumeFromSuspension(error);
            }
        });

    private void RunWorker()
    {
        while (_ready.Dequeue(out var actor)) {
            actor.ProcessBatch();
        }
    }

    private void RunWorker(int workerId)
    {
        while (_ready.Dequeue(out var actor)) {
            actor.ProcessBatch();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        _ready.Complete();
        _blocking.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class BlockingExecutor : IDisposable
    {
        private readonly BlockingQueue<Action> _work = new();
        private int _disposed;

#if !BROWSER
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
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);

#if BROWSER
            _ = Task.Run(action);
#else
            _work.Enqueue(action);
#endif
        }

        private void RunWorker()
        {
            while (_work.Dequeue(out var action)) {
                action();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }
            _work.Complete();
        }
    }
}
