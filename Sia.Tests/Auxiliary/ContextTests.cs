namespace Sia.Tests.Auxiliary;

public class ContextTests
{
    private interface IAssertService
    {
        void Execute(string expected);
    }

    private class TestRunner(string actualThread, int degreeOfParallelism)
        : ParallelRunner(degreeOfParallelism)
    {
        private class MockAssertService(string actual) : IAssertService
        {
            public void Execute(string expected) => Assert.Equal(expected, actual);
        }

        protected override void RunWorkerThread(int id, BlockingQueue<IJob> jobs)
        {
            Context<IAssertService>.Current = new MockAssertService(actualThread);
            Assert.NotNull(Context.Get<IAssertService>());
            base.RunWorkerThread(id, jobs);
        }
    }

    [Fact]
    public async Task ContextValue_ValueAssign_Test()
    {
        // Arrange
        const string valueForThread1 = "ThreadValue1";
        const string valueForThread2 = "ThreadValue2";
        var result1 = string.Empty;
        var result2 = string.Empty;

        var task1 = Task.Run(() => {
            Context<string>.Current = valueForThread1;
            result1 = Context.Get<string>();
        });

        var task2 = Task.Run(() => {
            Context<string>.Current = valueForThread2;
            result2 = Context.Get<string>();
        });

        // Act
        await Task.WhenAll(task1, task2);

        // Assert
        Assert.Equal(valueForThread1, result1);
        Assert.Equal(valueForThread2, result2);
    }

    [Fact]
    public void ContextValue_IJobAssign_Test()
    {
        // Arrange
        const string valueForThread1 = "ThreadValue1";
        const string valueForThread2 = "ThreadValue2";

        var runner1 = new TestRunner(valueForThread1, 5);
        var runner2 = new TestRunner(valueForThread2, 5);

        var barrier = RunnerBarrier.Get();

        // Assert
        runner1.Run(() => Context.Get<IAssertService>().Execute(valueForThread1), barrier);
        runner2.Run(() => Context.Get<IAssertService>().Execute(valueForThread2), barrier);
        barrier.WaitAndReturn();
    }
}