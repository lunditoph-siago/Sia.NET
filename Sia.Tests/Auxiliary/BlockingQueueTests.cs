namespace Sia.Tests.Auxiliary;

public class BlockingQueueTests
{
    [Fact]
    public void BlockingQueue_Enqueue_Test()
    {
        var queue = new BlockingQueue<int>();
        queue.Enqueue(1);

        Assert.True(queue.TryPeek(out var result));
        Assert.Equal(1, result);
    }

    [Fact]
    public void BlockingQueue_Complete_Test()
    {
        var queue = new BlockingQueue<int>();
        queue.Complete();

        Assert.Throws<InvalidOperationException>(() => queue.Enqueue(1));
    }

    [Fact]
    public async Task BlockingQueue_Complete_UnblocksDequeue_Test()
    {
        var queue = new BlockingQueue<int>();
        var task = Task.Run(() => Assert.False(queue.Dequeue(out _)));

        queue.Complete();
        await task;
    }

    [Fact]
    public async Task BlockingQueue_Dequeue_BlocksUntilItemIsAvailable_Test()
    {
        var queue = new BlockingQueue<int>();
        using var reachedDequeue = new ManualResetEventSlim();
        var dequeued = new TaskCompletionSource<(bool Success, int Item)>();

        var worker = Task.Run(() => {
            reachedDequeue.Set();
            var success = queue.Dequeue(out var item);
            dequeued.SetResult((success, item));
        });

        reachedDequeue.Wait();
        Assert.False(dequeued.Task.IsCompleted);

        queue.Enqueue(2);

        var (success, item) = await dequeued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(success);
        Assert.Equal(2, item);

        await worker;
    }
}