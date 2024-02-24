
namespace Sia;

public static class RunnerBarriers
{
    private class EmptyRunnerBarrier : IRunnerBarrier
    {
        public Exception? Exception => null;

        public void Signal() {}
        public void Throw(Exception e) {}
        public void WaitAndReturn() {}
    }

    public static readonly IRunnerBarrier Empty = new EmptyRunnerBarrier();
}