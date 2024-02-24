namespace Sia;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.ObjectPool;

public class ParallelRunner : IRunner
{
    private abstract class JobBase : IJob, IResettable
    {
        public IRunnerBarrier Barrier { get; set; } = null!;

        public abstract void Invoke();

        public virtual bool TryReset()
        {
            Barrier = null!;
            return true;
        }
    }

    private unsafe abstract class GroupJobBase : JobBase
    {
        public (int, int) Range { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void FinishJob() => Barrier.Signal();
    }

    private class ActionJob : JobBase
    {
        public Action Action = default!;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Invoke() {
            Action();
            Barrier.Signal();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Invoke() {
            Action(Data);
            Barrier.Signal();
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

    private class Barrier : IRunnerBarrier
    {
        private class PoolPolicy : IPooledObjectPolicy<Barrier>
        {
            public Barrier Create() => new();
            public bool Return(Barrier barrier)
            {
                var raw = barrier.Raw;
                raw.RemoveParticipants(raw.ParticipantCount);
                barrier.Exception = null;
                barrier.Callback = null;
                barrier.Argument = null;
                return true;
            }
        }

        public System.Threading.Barrier Raw { get; } = new(0);
        public Exception? Exception { get; private set; }

        public Action<object?>? Callback { get; set; }
        public object? Argument { get; set; }

        private readonly static DefaultObjectPool<Barrier> s_barrierPool = new(new PoolPolicy());

        public static Barrier Get(int taskCount)
        {
            var barrier = s_barrierPool.Get();
            barrier.Raw.AddParticipants(taskCount + 1);
            return barrier;
        }

        private Barrier() {}

        public void Signal()
        {
            Raw.RemoveParticipant();
        }

        public void Throw(Exception e)
        {
            Exception = e;
            Raw.RemoveParticipant();
        }

        public void WaitAndReturn()
        {
            Raw.SignalAndWait();
            Callback?.Invoke(Argument);

            if (Exception != null) {
                s_barrierPool.Return(this);
                throw Exception;
            }
            else {
                s_barrierPool.Return(this);
            }
        }
    }
    
    public static readonly ParallelRunner Default = new(Environment.ProcessorCount);

    public int DegreeOfParallelism { get; }

    private readonly Channel<IJob> _jobChannel = Channel.CreateUnbounded<IJob>();

    private readonly DefaultObjectPool<GroupActionJob[]> _groupActionJobArrPool;
    private readonly ConcurrentDictionary<Type, object> _genericGroupActionJobArrPools = [];

    private readonly static ObjectPool<ActionJob> s_actionJobPool = ObjectPool.Create<ActionJob>();
    private readonly static ConcurrentDictionary<Type, object> s_genericActionJobPool = [];

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
                job.Barrier.Throw(e);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe IRunnerBarrier Run(Action action)
    {
        var taskWriter = _jobChannel.Writer;

        var barrier = Barrier.Get(1);

        var job = s_actionJobPool.Get();
        job.Barrier = barrier;
        job.Action = action;

        var callback = s_actionJobPool.Return;
        barrier.Callback = Unsafe.As<Action<object?>>(callback);
        barrier.Argument = job;

        taskWriter.TryWrite(job);
        return barrier;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe IRunnerBarrier Run<TData>(in TData data, InAction<TData> action)
    {
        var taskWriter = _jobChannel.Writer;
        var barrier = Barrier.Get(1);
        
        if (!s_genericActionJobPool.TryGetValue(typeof(TData), out var poolRaw)) {
            poolRaw = s_genericActionJobPool.GetOrAdd(typeof(TData),
                static _ => ObjectPool.Create<ActionJob<TData>>());
        }

        var pool = Unsafe.As<ObjectPool<ActionJob<TData>>>(poolRaw);

        var job = pool.Get();
        job.Data = data;
        job.Action = action;

        var callback = pool.Return;
        barrier.Callback = Unsafe.As<Action<object?>>(callback);
        barrier.Argument = job;

        taskWriter.TryWrite(job);
        return barrier;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe IRunnerBarrier Run(int taskCount, GroupAction action)
    {
        var taskWriter = _jobChannel.Writer;
        var degreeOfParallelism = Math.Min(taskCount, DegreeOfParallelism);

        var div = taskCount / degreeOfParallelism;
        var remaining = taskCount % degreeOfParallelism;
        var acc = 0;

        var barrier = Barrier.Get(degreeOfParallelism);
        var jobs = _groupActionJobArrPool.Get();

        var callback = _groupActionJobArrPool.Return;
        barrier.Callback = Unsafe.As<Action<object?>>(callback);
        barrier.Argument = jobs;

        for (int i = 0; i != degreeOfParallelism; ++i) {
            int start = acc;
            acc += i < remaining ? div + 1 : div;

            var job = jobs[i];
            job.Range = (start, acc);
            job.Barrier = barrier;
            job.Action = action;
            taskWriter.TryWrite(job);
        }

        return barrier;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe IRunnerBarrier Run<TData>(int taskCount, in TData data, GroupAction<TData> action)
    {
        var taskWriter = _jobChannel.Writer;
        var degreeOfParallelism = Math.Min(taskCount, DegreeOfParallelism);

        var div = taskCount / degreeOfParallelism;
        var remaining = taskCount % degreeOfParallelism;
        var acc = 0;

        static object CreateJobArrayPool(ParallelRunner runner)
            => new DefaultObjectPool<GroupActionJob<TData>[]>(
                new JobArrayPolicy<GroupActionJob<TData>>(runner));
        
        if (!_genericGroupActionJobArrPools.TryGetValue(typeof(TData), out var poolRaw)) {
            poolRaw = _genericGroupActionJobArrPools.GetOrAdd(typeof(TData),
                _ => CreateJobArrayPool(this));
        }

        var barrier = Barrier.Get(degreeOfParallelism);
        var pool = Unsafe.As<DefaultObjectPool<GroupActionJob<TData>[]>>(poolRaw);
        var jobs = pool.Get();

        var callback = pool.Return;
        barrier.Callback = Unsafe.As<Action<object?>>(callback);
        barrier.Argument = jobs;

        for (int i = 0; i != degreeOfParallelism; ++i) {
            int start = acc;
            acc += i < remaining ? div + 1 : div;

            var job = jobs[i];
            job.Range = (start, acc);
            job.Barrier = barrier;
            job.Data = data;
            job.Action = action;
            taskWriter.TryWrite(job);
        }

        return barrier;
    }

    public void Dispose()
    {
        _jobChannel.Writer.Complete();
        GC.SuppressFinalize(this);
    }
}