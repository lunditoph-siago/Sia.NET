namespace Sia_Examples;

using System.Collections.Concurrent;
using Sia;

public static class Example15_RunnerWithContext
{
    private interface ITestService
    {
        void Test();
    }

    private class TestRunner(string name, int degreeOfParallelism)
        : ParallelRunner(degreeOfParallelism)
    {
        private class TestService(string name) : ITestService
        {
            public void Test()
            {
                Console.WriteLine("Test service called on " + name);
            }
        }

        protected override void RunWorkerThread(int id, BlockingCollection<IJob> jobs)
        {
            Context<ITestService>.Current = new TestService(name);
            Console.WriteLine(Context.Get<ITestService>());
            base.RunWorkerThread(id, jobs);
        }
    }

    public static void Run(World world)
    {
        var runner1 = new TestRunner("Runner1", 5);
        var runner2 = new TestRunner("Runner2", 5);

        var barrier = RunnerBarrier.Get();
        runner1.Run(() => {
            Context.Get<ITestService>().Test();
        }, barrier);
        runner2.Run(() => {
            Context.Get<ITestService>().Test();
        }, barrier);
        barrier.WaitAndReturn();
    }
}