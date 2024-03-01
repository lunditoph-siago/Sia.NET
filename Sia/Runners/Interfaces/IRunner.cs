namespace Sia;

public interface IRunner : IDisposable
{
    void Run(Action action, RunnerBarrier? barrier = null);
    void Run<TData>(in TData data, InAction<TData> action, RunnerBarrier? barrier = null);
    void Run(int taskCount, GroupAction action, RunnerBarrier? barrier = null);
    void Run<TData>(int taskCount, in TData data, GroupAction<TData> action, RunnerBarrier? barrier = null);
}