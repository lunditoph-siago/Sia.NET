namespace Sia;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.ObjectPool;

public sealed class ParallelRunner<TData> : IRunner<TData>
{
    private unsafe class TaskEntry
    {
        public TData Data = default!;
        public (int, int) Range;
        public RunnerAction<TData> Action = default!;
        public RunnerBarrier Barrier = default!;
        public int* RemainingTaskGroups;
    }

    private class TaskEntryArrayPolicy(ParallelRunner<TData> runner) : IPooledObjectPolicy<TaskEntry[]>
    {
        public TaskEntry[] Create()
        {
            var arr = new TaskEntry[runner.DegreeOfParallelism];
            foreach (ref var entry in arr.AsSpan()) {
                entry = new();
            }
            return arr;
        }

        public bool Return(TaskEntry[] arr)
        {
            foreach (ref var entry in arr.AsSpan()) {
                entry.Data = default!;
                entry.Action = default!;
                entry.Barrier = default!;
            }
            return true;
        }
    }

    private class RunnerBarrier() : Barrier(2);
    
    public static readonly ParallelRunner<TData> Default = new(Environment.ProcessorCount);

    public int DegreeOfParallelism { get; }

    private readonly Channel<TaskEntry> _taskChannel = Channel.CreateUnbounded<TaskEntry>();
    private readonly ObjectPool<TaskEntry[]> _taskArrayPool;

    private readonly static ObjectPool<RunnerBarrier> s_barrierPool
        = ObjectPool.Create<RunnerBarrier>();

    public ParallelRunner(int degreeOfParallelism)
    {
        DegreeOfParallelism = degreeOfParallelism;
        _taskArrayPool = new DefaultObjectPool<TaskEntry[]>(new TaskEntryArrayPolicy(this));

        var reader = _taskChannel.Reader;
        for (int i = 0; i != DegreeOfParallelism; ++i) {
            Task.Factory.StartNew(async () => {
                await foreach (var task in reader.ReadAllAsync()) {
                    task.Action(task.Data, task.Range);
                    unsafe {
                        if (Interlocked.Decrement(ref *task.RemainingTaskGroups) == 0) {
                            task.Barrier.SignalAndWait();
                        }
                    }
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    ~ParallelRunner()
    {
        _taskChannel.Writer.Complete();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Run(int taskCount, in TData data, RunnerAction<TData> action)
    {
        var taskWriter = _taskChannel.Writer;

        var div = taskCount / DegreeOfParallelism;
        var remaining = taskCount % DegreeOfParallelism;
        var acc = 0;

        if (div == 0) {
            action(data, (0, taskCount));
            return;
        }

        int remainingTaskGroups = DegreeOfParallelism;
        var barrier = s_barrierPool.Get();
        var tasks = _taskArrayPool.Get();

        for (int i = 0; i != DegreeOfParallelism; ++i) {
            int start = acc;
            acc += i < remaining ? div + 1 : div;

            var task = tasks[i];
            task.Data = data;
            task.Range = (start, acc);
            task.Action = action;
            task.Barrier = barrier;
            task.RemainingTaskGroups = &remainingTaskGroups;
            taskWriter.TryWrite(task);
        }

        barrier.SignalAndWait();

        _taskArrayPool.Return(tasks);
        s_barrierPool.Return(barrier);
    }

    public void Dispose()
    {
        _taskChannel.Writer.Complete();
        GC.SuppressFinalize(this);
    }
}