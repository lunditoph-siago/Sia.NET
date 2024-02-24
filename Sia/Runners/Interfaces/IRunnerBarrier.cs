namespace Sia;

public interface IRunnerBarrier
{
    Exception? Exception { get; }

    void Signal();
    void Throw(Exception e);
    void WaitAndReturn();
}