namespace Sia;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.ObjectPool;

public class ParallelRunner : IRunner
{
    private abstract class JobBase : IJob, IResettable
    {
        public Barrier Barrier { get; set; } = null!;
        internal Exception? Exception { get; private set; }

        public abstract void Invoke();

        public virtual void Throw(Exception e)
        {
            Exception = e;
        }

        public virtual bool TryReset()
        {
            Barrier = null!;
            Exception = null;
            return true;
        }
    }

    private unsafe abstract class GroupJobBase : JobBase
    {
        public (int, int) Range { get; set; }
        public int* RemainingTaskGroups { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void FinishJob()
        {
            if (Interlocked.Decrement(ref *RemainingTaskGroups) == 0) {
                Barrier.SignalAndWait();
            }
        }

        public override void Throw(Exception e)
        {
            base.Throw(e);

            if (Interlocked.Decrement(ref *RemainingTaskGroups) == 0) {
                Barrier.SignalAndWait();
            }
        }
    }

    private class ActionJob : JobBase
    {
        public Action Action = default!;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Invoke() {
            Action();
            Barrier.SignalAndWait();
        }

        public override void Throw(Exception e)
        {
            base.Throw(e);
            Barrier.SignalAndWait();
        }

        public override bool TryReset()
        {
            Action = default!;
            return base.TryReset();
        }
    }
    
    private class GroupActionJob : GroupJobBase
    {
        public GroupAction Action = default!;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    private unsafe class GroupActionJob<TData> : GroupJobBase
    {
        public TData Data = default!;
        public GroupAction<TData> Action = default!;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            var arr = new TJob[runner.DegreeOfParallelism];
            foreach (ref var entry in arr.AsSpan()) {
                entry = new();
            }
            return arr;
        }

        public bool Return(TJob[] arr)
        {
            foreach (ref var entry in arr.AsSpan()) {
                entry.TryReset();
            }
            return true;
        }
    }

    private class RunnerBarrier() : Barrier(2);
    
    public static readonly ParallelRunner Default = new(Environment.ProcessorCount);

    public int DegreeOfParallelism { get; }

    private readonly Channel<IJob> _jobChannel = Channel.CreateUnbounded<IJob>();

    private readonly ObjectPool<ActionJob> _actionJobArrPool = ObjectPool.Create<ActionJob>();
    private readonly DefaultObjectPool<GroupActionJob[]> _groupActionJobArrPool;
    private readonly ConcurrentDictionary<Type, object> _genericGroupActionJobArrPools = [];

    private readonly static ObjectPool<RunnerBarrier> s_barrierPool
        = ObjectPool.Create<RunnerBarrier>();

    public ParallelRunner(int degreeOfParallelism)
    {
        DegreeOfParallelism = degreeOfParallelism;
        _groupActionJobArrPool = new(new JobArrayPolicy<GroupActionJob>(this));

        var reader = _jobChannel.Reader;
        for (int i = 0; i != DegreeOfParallelism; ++i) {
            Task.Factory.StartNew(
                () => RunWorkerThreadAsync(i, reader),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    ~ParallelRunner()
    {
        _jobChannel.Writer.Complete();
    }

    protected virtual async Task RunWorkerThreadAsync(int id, ChannelReader<IJob> reader)
    {
        Thread.CurrentThread.Name = "ParallelRunner Worker " + id;

        await foreach (var job in reader.ReadAllAsync()) {
            try {
                job.Invoke();
            }
            catch (Exception e) {
                job.Throw(e);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Run(Action action)
    {
        var taskWriter = _jobChannel.Writer;

        var barrier = s_barrierPool.Get();
        var job = _actionJobArrPool.Get();

        job.Barrier = barrier;
        job.Action = action;

        taskWriter.TryWrite(job);
        barrier.SignalAndWait();
        s_barrierPool.Return(barrier);

        var e = job.Exception;
        _actionJobArrPool.Return(job);

        if (e != null) { throw e; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Run(int taskCount, GroupAction action)
    {
        var taskWriter = _jobChannel.Writer;

        var div = taskCount / DegreeOfParallelism;
        var remaining = taskCount % DegreeOfParallelism;
        var acc = 0;

        if (div == 0) {
            action((0, taskCount));
            return;
        }

        int remainingTaskGroups = DegreeOfParallelism;
        var barrier = s_barrierPool.Get();
        var jobs = _groupActionJobArrPool.Get();

        for (int i = 0; i != DegreeOfParallelism; ++i) {
            int start = acc;
            acc += i < remaining ? div + 1 : div;

            var job = jobs[i];
            job.Range = (start, acc);
            job.Barrier = barrier;
            job.RemainingTaskGroups = &remainingTaskGroups;
            job.Action = action;
            taskWriter.TryWrite(job);
        }

        barrier.SignalAndWait();
        s_barrierPool.Return(barrier);

        try {
            foreach (var job in jobs) {
                if (job.Exception != null) {
                    throw job.Exception;
                }
            }
        }
        finally {
            _groupActionJobArrPool.Return(jobs);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Run<TData>(int taskCount, in TData data, GroupAction<TData> action)
    {
        var taskWriter = _jobChannel.Writer;

        var div = taskCount / DegreeOfParallelism;
        var remaining = taskCount % DegreeOfParallelism;
        var acc = 0;

        if (div == 0) {
            action(data, (0, taskCount));
            return;
        }

        int remainingTaskGroups = DegreeOfParallelism;
        var barrier = s_barrierPool.Get();

        static object CreateJobArrayPool(ParallelRunner runner)
            => new DefaultObjectPool<GroupActionJob<TData>[]>(
                new JobArrayPolicy<GroupActionJob<TData>>(runner));
        
        if (!_genericGroupActionJobArrPools.TryGetValue(typeof(TData), out var poolRaw)) {
            poolRaw = _genericGroupActionJobArrPools.GetOrAdd(typeof(TData),
                _ => CreateJobArrayPool(this));
        }

        var pool = Unsafe.As<DefaultObjectPool<GroupActionJob<TData>[]>>(poolRaw);
        var jobs = pool.Get();

        for (int i = 0; i != DegreeOfParallelism; ++i) {
            int start = acc;
            acc += i < remaining ? div + 1 : div;

            var job = jobs[i];
            job.Range = (start, acc);
            job.Barrier = barrier;
            job.RemainingTaskGroups = &remainingTaskGroups;
            job.Data = data;
            job.Action = action;
            taskWriter.TryWrite(job);
        }

        barrier.SignalAndWait();
        s_barrierPool.Return(barrier);

        try {
            foreach (var job in jobs) {
                if (job.Exception != null) {
                    throw job.Exception;
                }
            }
        }
        finally {
            pool.Return(jobs);
        }
    }

    public void Dispose()
    {
        _jobChannel.Writer.Complete();
        GC.SuppressFinalize(this);
    }
}