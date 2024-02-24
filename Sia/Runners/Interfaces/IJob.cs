namespace Sia;

public interface IJob
{
    IRunnerBarrier Barrier { get; }
    void Invoke();
}