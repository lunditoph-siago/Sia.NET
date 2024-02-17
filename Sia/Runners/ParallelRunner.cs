namespace Sia;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.ObjectPool;

public sealed class ParallelRunner<TData> : IRunner<TData>
{
    private unsafe readonly struct TaskEntry(
        TData data, (int, int) range, RunnerAction<TData> action, RunnerBarrier barrier, int* remainingTaskCount)
    {
        public readonly TData Data = data;
        public readonly (int, int) Range = range;
        public readonly RunnerAction<TData> Action = action;
        public readonly RunnerBarrier Barrier = barrier;
        public readonly int* RemainingTaskCount = remainingTaskCount;
    }
    
    private class RunnerBarrier() : Barrier(2);
    
    public static readonly ParallelRunner<TData> Default = new(Environment.ProcessorCount);

    public int DegreeOfParallelism { get; }

    private readonly Channel<TaskEntry> _taskChannel = Channel.CreateUnbounded<TaskEntry>();
    private readonly static ObjectPool<RunnerBarrier> s_barrierPool = ObjectPool.Create<RunnerBarrier>();

    public ParallelRunner(int degreeOfParallelism)
    {
        DegreeOfParallelism = degreeOfParallelism;

        var reader = _taskChannel.Reader;
        for (int i = 0; i != DegreeOfParallelism; ++i) {
            Task.Factory.StartNew(async () => {
                await foreach (var task in reader.ReadAllAsync()) {
                    task.Action(task.Data, task.Range);
                    unsafe {
                        if (Interlocked.Decrement(ref *task.RemainingTaskCount) == 0) {
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

        int remainingTaskCount = DegreeOfParallelism;
        var barrier = s_barrierPool.Get();

        for (int i = 0; i != DegreeOfParallelism; ++i) {
            int start = acc;
            acc += i < remaining ? div + 1 : div;

            var task = new TaskEntry(data, (start, acc), action, barrier, &remainingTaskCount);
            if (!taskWriter.TryWrite(task)) {
                throw new Exception("Failed to send cluster task, this should not happen.");
            }
        }

        barrier.SignalAndWait();
        s_barrierPool.Return(barrier);
    }

    public void Dispose()
    {
        _taskChannel.Writer.Complete();
        GC.SuppressFinalize(this);
    }
}