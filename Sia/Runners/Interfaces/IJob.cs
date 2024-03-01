namespace Sia;

public interface IJob
{
    RunnerBarrier? Barrier { get; }
    void Invoke();
}