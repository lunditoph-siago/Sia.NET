using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.ObjectPool;

namespace Sia;

public class ParallelRunner : IRunner
{
    private abstract class JobBase : IJob, IResettable
    {
        public RunnerBarrier? Barrier { get; set; } = null;

        public abstract void Invoke();

        public virtual bool TryReset()
        {
            Barrier = null!;
            return true;
        }
    }

    private abstract class GroupJobBase : JobBase
    {
        public (int, int) Range { get; set; }

        protected void FinishJob() => Barrier?.Signal();
    }

    private class ActionJob : JobBase
    {
        public Action Action = default!;

        public override void Invoke()
        {
            Action();
            Barrier?.Signal();
            s_ActionJobPool.Return(this);
        }

        public override bool TryReset()
        {
            Action = default!;
            return base.TryReset();
        }
    }

    private class ActionJob<TData> : JobBase
    {
        public InAction<TData> Action = default!;
        public TData Data = default!;

        public override void Invoke()
        {
            Action(Data);
            Barrier?.Signal();

            if (s_GenericActionJobPools.TryGetValue(typeof(TData), out var untypedPool)) {
                var pool = Unsafe.As<ObjectPool<ActionJob<TData>>>(untypedPool);
                pool.Return(this);
            }
        }

        public override bool TryReset()
        {
            Action = default!;
            Data = default!;
            return base.TryReset();
        }
    }

    private class GroupActionJob : GroupJobBase
    {
        public GroupAction Action = default!;

        public override void Invoke()
        {
            Action(Range);
            FinishJob();
        }

        public override bool TryReset()
        {
            Action = default!;
            return base.TryReset();
        }
    }

    private class GroupActionJob<TData> : GroupJobBase
    {
        public TData Data = default!;
        public GroupAction<TData> Action = default!;

        public override void Invoke()
        {
            Action(Data, Range);
            FinishJob();
        }

        public override bool TryReset()
        {
            Data = default!;
            Action = null!;
            return base.TryReset();
        }
    }

    private class JobArrayPolicy<TJob>(ParallelRunner runner) : IPooledObjectPolicy<TJob[]>
        where TJob : JobBase, new()
    {
        public TJob[] Create()
        {
            var jobs = new TJob[runner.DegreeOfParallelism];
            foreach (ref var job in jobs.AsSpan()) {
                job = new();
            }
            return jobs;
        }

        public bool Return(TJob[] jobs)
        {
            foreach (ref var job in jobs.AsSpan()) {
                job.TryReset();
            }
            return true;
        }
    }

    public static readonly ParallelRunner Default = new(Environment.ProcessorCount);

    public int DegreeOfParallelism { get; }

    private readonly BlockingQueue<IJob> _jobs = new();
    private readonly object _submissionGate = new();

    private readonly DefaultObjectPool<GroupActionJob[]> _groupActionJobArrayPool;
    private readonly ConcurrentDictionary<Type, object> _genericGroupActionJobArrayPools = [];

    private static readonly ObjectPool<ActionJob> s_ActionJobPool = ObjectPool.Create<ActionJob>();
    private static readonly ConcurrentDictionary<Type, object> s_GenericActionJobPools = [];

    public ParallelRunner(int degreeOfParallelism)
    {
        DegreeOfParallelism = degreeOfParallelism;
        _groupActionJobArrayPool = new(new JobArrayPolicy<GroupActionJob>(this));

#if !BROWSER
        for (var i = 0; i != DegreeOfParallelism; ++i) {
            var workerId = i;
            Task.Factory.StartNew(
                () => RunWorkerThread(workerId, _jobs),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
#endif
    }

#if BROWSER
    private int _drainerIds;

    private void SpawnDrainers(int count)
    {
        for (var i = 0; i != count; ++i) {
            var workerId = Interlocked.Increment(ref _drainerIds) - 1;
            Task.Run(() => RunWorkerThread(workerId, _jobs));
        }
    }
#endif

    ~ParallelRunner()
    {
        _jobs.Complete();
    }

    protected virtual void RunWorkerThread(int id, BlockingQueue<IJob> jobs)
    {
        Thread.CurrentThread.Name = "ParallelRunner Worker " + id;

        while (jobs.Dequeue(out var job)) {
            try {
                job.Invoke();
            }
            catch (Exception error) {
                var barrier = job.Barrier;
                if (barrier != null) {
                    barrier.Throw(error);
                }
                else {
                    Console.Error.WriteLine($"[{GetType()}] Uncaught exception: " + error);
                }
            }
        }
    }

    public void Run(Action action, RunnerBarrier? barrier = null)
    {
        lock (_submissionGate) {
            ThrowIfClosed();
            var job = s_ActionJobPool.Get();
            job.Action = action;

            if (barrier != null) {
                job.Barrier = barrier;
                barrier.AddParticipants(1);
            }

            _jobs.Enqueue(job);
        }
#if BROWSER
        SpawnDrainers(1);
#endif
    }

    public void Run<TData>(in TData data, InAction<TData> action, RunnerBarrier? barrier = null)
    {
        lock (_submissionGate) {
            ThrowIfClosed();
            if (!s_GenericActionJobPools.TryGetValue(typeof(TData), out var untypedPool)) {
                untypedPool = s_GenericActionJobPools.GetOrAdd(typeof(TData),
                    static _ => ObjectPool.Create<ActionJob<TData>>());
            }

            var pool = Unsafe.As<ObjectPool<ActionJob<TData>>>(untypedPool);

            var job = pool.Get();
            job.Data = data;
            job.Action = action;

            if (barrier != null) {
                job.Barrier = barrier;
                barrier.AddParticipants(1);
            }

            _jobs.Enqueue(job);
        }
#if BROWSER
        SpawnDrainers(1);
#endif
    }

    public void Run(int taskCount, GroupAction action, RunnerBarrier? barrier = null)
    {
        int jobCount;
        lock (_submissionGate) {
            ThrowIfClosed();
            jobCount = EnqueueGroupUnderLock(taskCount, action, barrier);
        }
#if BROWSER
        SpawnDrainers(jobCount);
#endif
    }

    private int EnqueueGroupUnderLock(int taskCount, GroupAction action, RunnerBarrier? barrier)
    {
        var jobCount = Math.Min(taskCount, DegreeOfParallelism);
        var tasksPerJob = taskCount / jobCount;
        var extraTasks = taskCount % jobCount;
        var rangeEnd = 0;

        if (barrier != null) {
            var jobs = _groupActionJobArrayPool.Get();

            var callback = _groupActionJobArrayPool.Return;
            barrier.AddCallback(Unsafe.As<Action<object?>>(callback), jobs);
            barrier.AddParticipants(jobCount);

            for (var i = 0; i != jobCount; ++i) {
                var rangeStart = rangeEnd;
                rangeEnd += i < extraTasks ? tasksPerJob + 1 : tasksPerJob;

                var job = jobs[i];
                job.Barrier = barrier;
                job.Range = (rangeStart, rangeEnd);
                job.Action = action;
                _jobs.Enqueue(job);
            }
        }
        else {
            for (var i = 0; i != jobCount; ++i) {
                var rangeStart = rangeEnd;
                rangeEnd += i < extraTasks ? tasksPerJob + 1 : tasksPerJob;

                var job = new GroupActionJob {
                    Range = (rangeStart, rangeEnd),
                    Action = action
                };
                _jobs.Enqueue(job);
            }
        }
        return jobCount;
    }

    public void Run<TData>(
        int taskCount, in TData data, GroupAction<TData> action, RunnerBarrier? barrier = null)
    {
        int jobCount;
        lock (_submissionGate) {
            ThrowIfClosed();
            jobCount = EnqueueGroupUnderLock(taskCount, data, action, barrier);
        }
#if BROWSER
        SpawnDrainers(jobCount);
#endif
    }

    private int EnqueueGroupUnderLock<TData>(
        int taskCount, in TData data, GroupAction<TData> action, RunnerBarrier? barrier)
    {
        var jobCount = Math.Min(taskCount, DegreeOfParallelism);
        var tasksPerJob = taskCount / jobCount;
        var extraTasks = taskCount % jobCount;
        var rangeEnd = 0;

        if (barrier != null) {
            static object CreateJobArrayPool(ParallelRunner runner)
                => new DefaultObjectPool<GroupActionJob<TData>[]>(
                    new JobArrayPolicy<GroupActionJob<TData>>(runner));

            if (!_genericGroupActionJobArrayPools.TryGetValue(typeof(TData), out var untypedPool)) {
                untypedPool = _genericGroupActionJobArrayPools.GetOrAdd(typeof(TData),
                    _ => CreateJobArrayPool(this));
            }

            var pool = Unsafe.As<DefaultObjectPool<GroupActionJob<TData>[]>>(untypedPool);
            var jobs = pool.Get();

            var callback = pool.Return;
            barrier.AddCallback(Unsafe.As<Action<object?>>(callback), jobs);
            barrier.AddParticipants(jobCount);

            for (var i = 0; i != jobCount; ++i) {
                var rangeStart = rangeEnd;
                rangeEnd += i < extraTasks ? tasksPerJob + 1 : tasksPerJob;

                var job = jobs[i];
                job.Range = (rangeStart, rangeEnd);
                job.Barrier = barrier;
                job.Data = data;
                job.Action = action;
                _jobs.Enqueue(job);
            }
        }
        else {
            for (var i = 0; i != jobCount; ++i) {
                var rangeStart = rangeEnd;
                rangeEnd += i < extraTasks ? tasksPerJob + 1 : tasksPerJob;

                var job = new GroupActionJob<TData> {
                    Range = (rangeStart, rangeEnd),
                    Data = data,
                    Action = action
                };
                _jobs.Enqueue(job);
            }
        }
        return jobCount;
    }

    private void ThrowIfClosed()
        => ObjectDisposedException.ThrowIf(_jobs.IsCompleted, this);

    public void Dispose()
    {
        lock (_submissionGate) {
            _jobs.Complete();
        }

        GC.SuppressFinalize(this);
    }
}
