namespace Sia;

public interface IRunner<TData> : IDisposable
{
    void Run(int taskCount, in TData data, RunnerAction<TData> action);
}