namespace Sia.Tests.Runners;

public class RunnerTests
{
    [Fact]
    public void CurrentThreadRunner_Run_ExecutesActionSynchronously()
    {
        var runner = CurrentThreadRunner.Instance;
        var executed = false;

        runner.Run(() => executed = true);

        Assert.True(executed);
    }

    [Fact]
    public void CurrentThreadRunner_Run_WithData_ExecutesAction()
    {
        var runner = CurrentThreadRunner.Instance;
        var result = 0;

        runner.Run(42, (in data) => result = data);

        Assert.Equal(42, result);
    }

    [Fact]
    public void CurrentThreadRunner_Run_GroupAction_PassesFullRange()
    {
        var runner = CurrentThreadRunner.Instance;
        (int, int) captured = (-1, -1);

        runner.Run(10, range => captured = range);

        Assert.Equal((0, 10), captured);
    }

    [Fact]
    public void CurrentThreadRunner_Run_GroupActionWithData_PassesDataAndFullRange()
    {
        var runner = CurrentThreadRunner.Instance;
        (int data, (int, int) range) captured = default;

        runner.Run(10, 99, (in d, r) => captured = (d, r));

        Assert.Equal(99, captured.data);
        Assert.Equal((0, 10), captured.range);
    }

    [Fact]
    public void CurrentThreadRunner_Run_IgnoresBarrier()
    {
        var runner = CurrentThreadRunner.Instance;
        var barrier = RunnerBarrier.Get();
        barrier.AddParticipants(1);

        runner.Run(() => { }, null);

        barrier.Signal();
        barrier.WaitAndReturn();
    }

    [Fact]
    public void CurrentThreadRunner_Dispose_DoesNotThrow()
    {
        CurrentThreadRunner.Instance.Dispose();
    }

    [Fact]
    public async Task RunnerBarrier_WaitAndReturn_CompletesWhenAllParticipantsSignal()
    {
        var barrier = RunnerBarrier.Get();
        barrier.AddParticipants(2);

        var t1 = Task.Run(barrier.Signal);
        var t2 = Task.Run(barrier.Signal);
        var waitTask = Task.Run(barrier.WaitAndReturn);

        await Task.WhenAll(t1, t2, waitTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RunnerBarrier_Throw_PropagatesException()
    {
        var barrier = RunnerBarrier.Get();
        barrier.AddParticipants(1);

        Task.Run(() => barrier.Throw(new InvalidOperationException("test error")));

        var ex = Assert.Throws<InvalidOperationException>(barrier.WaitAndReturn);
        Assert.Equal("test error", ex.Message);
    }

    [Fact]
    public void RunnerBarrier_AddCallback_InvokedAfterWait()
    {
        var barrier = RunnerBarrier.Get();
        var callbackInvoked = false;
        barrier.AddCallback(_ => callbackInvoked = true);
        barrier.AddParticipants(1);

        Task.Run(barrier.Signal);

        barrier.WaitAndReturn();

        Assert.True(callbackInvoked);
    }

    [Fact]
    public void RunnerBarrier_AddCallback_ReceivesUserData()
    {
        var barrier = RunnerBarrier.Get();
        object? received = null;
        var userData = new object();

        barrier.AddCallback(d => received = d, userData);
        barrier.AddParticipants(1);
        Task.Run(barrier.Signal);
        barrier.WaitAndReturn();

        Assert.Same(userData, received);
    }

    [Fact]
    public void RunnerBarrier_TooManyCallbacks_Throws()
    {
        var barrier = RunnerBarrier.Get();

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            for (var i = 0; i <= 32; i++)
            {
                barrier.AddCallback(_ => { });
            }
        });

        barrier.WaitAndReturn();

        Assert.Contains("32", ex.Message);
    }

    [Fact]
    public async Task ParallelRunner_Run_ExecutesAction()
    {
        using var runner = new ParallelRunner(2);
        var barrier = RunnerBarrier.Get();
        var executed = false;

        runner.Run(() => executed = true, barrier);

        await Task.Run(barrier.WaitAndReturn);

        Assert.True(executed);
    }

    [Fact]
    public async Task ParallelRunner_Run_WithData_ExecutesAction()
    {
        using var runner = new ParallelRunner(2);
        var barrier = RunnerBarrier.Get();
        var result = 0;

        runner.Run(42, (in data) => result = data, barrier);

        await Task.Run(barrier.WaitAndReturn);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ParallelRunner_Run_GroupAction_CoversAllItems()
    {
        using var runner = new ParallelRunner(4);
        const int total = 100;
        var counter = new int[total];
        var barrier = RunnerBarrier.Get();

        runner.Run(total, range =>
        {
            for (var i = range.Item1; i < range.Item2; i++)
            {
                Interlocked.Increment(ref counter[i]);
            }
        }, barrier);

        await Task.Run(barrier.WaitAndReturn);

        Assert.All(counter, v => Assert.Equal(1, v));
    }

    [Fact]
    public async Task ParallelRunner_Run_GroupActionWithData_CoversAllItems()
    {
        using var runner = new ParallelRunner(4);
        const int total = 100;
        var counter = new int[total];
        var barrier = RunnerBarrier.Get();

        runner.Run(total, counter, (in arr, range) =>
        {
            for (var i = range.Item1; i < range.Item2; i++)
            {
                Interlocked.Increment(ref arr[i]);
            }
        }, barrier);

        await Task.Run(barrier.WaitAndReturn);

        Assert.All(counter, v => Assert.Equal(1, v));
    }

    [Fact]
    public async Task ParallelRunner_Run_GroupAction_NoBarrier_DoesNotThrow()
    {
        using var runner = new ParallelRunner(4);
        const int total = 50;

        runner.Run(total, _ => { });

        await Task.Delay(200);
    }

    [Fact]
    public async Task ParallelRunner_ExceptionInJob_PropagatedThroughBarrier()
    {
        using var runner = new ParallelRunner(2);
        var barrier = RunnerBarrier.Get();

        runner.Run(() => throw new InvalidOperationException("parallel error"), barrier);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Run(() => barrier.WaitAndReturn()));

        Assert.Equal("parallel error", ex.Message);
    }

    [Fact]
    public async Task ParallelRunner_MultipleJobs_AllComplete()
    {
        using var runner = new ParallelRunner(4);
        const int jobCount = 20;
        var count = 0;
        var barrier = RunnerBarrier.Get();

        for (var i = 0; i < jobCount; i++)
        {
            runner.Run(() => Interlocked.Increment(ref count), barrier);
        }

        await Task.Run(barrier.WaitAndReturn);

        Assert.Equal(jobCount, count);
    }

    [Fact]
    public void ParallelRunner_Default_HasProcessorCountParallelism()
    {
        Assert.Equal(Environment.ProcessorCount, ParallelRunner.Default.DegreeOfParallelism);
    }

    [Fact]
    public void ParallelRunner_Dispose_DoesNotThrow()
    {
        var runner = new ParallelRunner(2);
        runner.Dispose();
    }
}
