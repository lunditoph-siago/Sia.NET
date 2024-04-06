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
    public void BlockingQueue_Complete_UnblocksDequeue_Test()
    {
        var queue = new BlockingQueue<int>();
        var task = Task.Run(() => Assert.False(queue.Dequeue(out _)));

        Thread.Sleep(100);
        queue.Complete();
        task.Wait();
    }

    [Fact]
    public void BlockingQueue_Dequeue_BlocksUntilItemIsAvailable_Test()
    {
        var queue = new BlockingQueue<int>();
        Task.Run(() => {
            Thread.Sleep(100);
            queue.Enqueue(2);
        });

        Assert.True(queue.Dequeue(out var item));
        Assert.Equal(2, item);
    }
}